using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TiaMcpServer.Runtime
{
    internal static class StandardPlcSimV16
    {
        private const string ApiAssemblyFileName = "Siemens.Simatic.PlcSim.VplcApi.dll";
        private const string ControllerTypeName = "Siemens.Simatic.PlcSim.VplcApi.SimulationController";

        private static readonly string[] StandardProcessNames =
        {
            "SIMATIC_S7PLCSIM_V16",
            "PlcSim.Opns",
            "Siemens.Simatic.PlcSim.Compact",
            "Siemens.Simatic.PlcSim.VplcHost"
        };

        private static readonly object ControllerGate = new object();
        private static ControllerHandle? _controllerHandle;

        internal static StandardPlcSimProbeResult Probe(string apiPath)
        {
            var data = new JsonObject
            {
                ["product"] = "SIMATIC S7-PLCSIM V16",
                ["apiAssemblyFileName"] = ApiAssemblyFileName,
                ["advancedExcluded"] = true,
                ["safety"] = new JsonObject
                {
                    ["readOnly"] = true,
                    ["startsRuntime"] = false,
                    ["createsCpu"] = false,
                    ["downloadsProgram"] = false,
                    ["changesCpuMode"] = false
                }
            };

            var candidates = ResolveApiCandidates(apiPath);
            var candidateNodes = new JsonArray();
            foreach (var candidate in candidates)
            {
                candidateNodes.Add(new JsonObject
                {
                    ["path"] = candidate,
                    ["exists"] = File.Exists(candidate)
                });
            }
            data["apiCandidates"] = candidateNodes;

            var selectedPath = candidates.FirstOrDefault(File.Exists);
            data["apiAssemblyPath"] = selectedPath;
            data["installed"] = selectedPath != null;

            var processCapture = CaptureProcesses(selectedPath);
            data["standardProcessRunning"] = processCapture.Running;
            data["standardProcesses"] = processCapture.Items;
            var processNameNodes = new JsonArray();
            foreach (var processName in StandardProcessNames)
                processNameNodes.Add(processName);
            data["standardProcessNames"] = processNameNodes;

            if (selectedPath == null)
            {
                return new StandardPlcSimProbeResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 API 未找到；未执行运行时连接或控制动作。",
                    Data = data
                };
            }

            try
            {
                var controllerHandle = GetController(selectedPath);
                var assembly = controllerHandle.Assembly;
                data["apiLoadOk"] = true;
                data["apiAssemblyFullName"] = assembly.FullName;

                var controllerType = controllerHandle.ControllerType;
                var controller = controllerHandle.Controller;
                data["controllerConstructed"] = controller != null;
                data["controllerType"] = controllerType.FullName;
                var methodNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    methodNames.Add(method.Name);
                var methodNodes = new JsonArray();
                foreach (var methodName in methodNames.OrderBy(name => name, StringComparer.Ordinal))
                    methodNodes.Add(methodName);
                data["controllerMethods"] = methodNodes;

                if (controller != null)
                {
                    var state = controllerType.GetProperty("State", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(controller, null);
                    data["controllerState"] = ReadState(state);
                    data["runtimeReady"] = IsRuntimeReady(state);
                }

                var message = processCapture.Running
                    ? "标准 S7-PLCSIM V16 API 已加载，并检测到标准进程；当前探测未证明 CPU 已连接、已下载或处于 RUN。"
                    : "标准 S7-PLCSIM V16 API 已加载，但未检测到标准 PLCSIM 进程；当前探测未证明仿真 CPU 已连接。";

                return new StandardPlcSimProbeResult
                {
                    Ok = true,
                    Message = message,
                    Data = data
                };
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                data["apiLoadOk"] = false;
                data["errorType"] = root.GetType().FullName;
                data["error"] = root.Message;
                data["diagnostic"] = root.ToString();
                return new StandardPlcSimProbeResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 API 文件已找到，但加载或控制器探测失败；未执行运行时控制动作。",
                    Data = data
                };
            }
        }

        internal static StandardPlcSimActionResult PowerOn(string apiPath, string storageDirectory)
        {
            var data = new JsonObject
            {
                ["product"] = "SIMATIC S7-PLCSIM V16",
                ["action"] = "PowerOn",
                ["family"] = "S71200",
                ["storageDirectory"] = storageDirectory,
                ["safety"] = new JsonObject
                {
                    ["startsRuntime"] = true,
                    ["createsCpu"] = true,
                    ["downloadsProgram"] = false,
                    ["changesCpuMode"] = false
                }
            };

            if (string.IsNullOrWhiteSpace(storageDirectory))
            {
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "storageDirectory 不能为空；未调用 PowerOn。",
                    Data = data
                };
            }

            var selectedPath = ResolveApiCandidates(apiPath).FirstOrDefault(File.Exists);
            data["apiAssemblyPath"] = selectedPath;
            if (selectedPath == null)
            {
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 API 未找到；未调用 PowerOn。",
                    Data = data
                };
            }

            try
            {
                var storagePath = Path.GetFullPath(storageDirectory.Trim());
                Directory.CreateDirectory(storagePath);
                data["storageDirectory"] = storagePath;

                // The V16 controller starts the Compact runtime by executable name.
                // Its installation directory is not guaranteed to be on PATH when
                // the MCP is launched by an MCP host.
                var runtimeBinDirectory = Path.GetDirectoryName(selectedPath);
                if (!string.IsNullOrWhiteSpace(runtimeBinDirectory))
                {
                    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    if (currentPath.IndexOf(runtimeBinDirectory, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Environment.SetEnvironmentVariable(
                            "PATH",
                            runtimeBinDirectory + Path.PathSeparator + currentPath);
                    }
                    data["runtimeBinDirectory"] = runtimeBinDirectory;
                }

                var handle = GetController(selectedPath);
                var powerOnMethod = handle.ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "PowerOn", StringComparison.Ordinal))
                            return false;

                        var parameters = method.GetParameters();
                        return parameters.Length == 4
                            && parameters[0].ParameterType.IsEnum
                            && parameters[2].ParameterType.IsEnum
                            && parameters[3].ParameterType == typeof(int);
                    });

                if (powerOnMethod == null)
                {
                    data["controllerType"] = handle.ControllerType.FullName;
                    return new StandardPlcSimActionResult
                    {
                        Ok = false,
                        Message = "SimulationController.PowerOn(Family, storageDirectory, CycleTimeSetting, Int32) 未找到；未执行控制动作。",
                        Data = data
                    };
                }

                var parameters = powerOnMethod.GetParameters();
                var family = Enum.Parse(parameters[0].ParameterType, "S71200");
                var cycleTimeSetting = Enum.Parse(parameters[2].ParameterType, "EnforceConfiguration");
                var rawResult = powerOnMethod.Invoke(
                    handle.Controller,
                    new object[] { family, storagePath, cycleTimeSetting, 0 });
                var powerOnResult = rawResult is bool result && result;
                data["powerOnReturn"] = ToJsonNode(rawResult);

                if (powerOnResult)
                {
                    var completionMethod = handle.ControllerType.GetMethod(
                        "PowerOnComplete",
                        BindingFlags.Public | BindingFlags.Instance,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);
                    if (completionMethod != null)
                    {
                        var completion = completionMethod.Invoke(handle.Controller, null) as Task;
                        if (completion != null && !completion.Wait(TimeSpan.FromSeconds(60)))
                        {
                            data["powerOnCompleteTimedOut"] = true;
                            return new StandardPlcSimActionResult
                            {
                                Ok = false,
                                Message = "PowerOn 已发起，但 60 秒内未完成；未继续下载。",
                                Data = data
                            };
                        }
                    }
                }

                var state = WaitForPoweredOn(handle.ControllerType, handle.Controller, TimeSpan.FromSeconds(15));
                data["controllerState"] = ReadState(state);
                data["runtimeReady"] = IsRuntimeReady(state);
                var poweredOn = ReadBooleanProperty(state, "IsPoweredOn");
                var ok = powerOnResult && poweredOn;

                return new StandardPlcSimActionResult
                {
                    Ok = ok,
                    Message = ok
                        ? "标准 S7-PLCSIM V16 已通过 PowerOn 进入上电待机状态；尚未下载程序或切换 RUN。"
                        : "PowerOn 未能确认标准仿真运行时进入上电状态；未继续下载。",
                    Data = data
                };
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                data["errorType"] = root.GetType().FullName;
                data["error"] = root.Message;
                data["diagnostic"] = root.ToString();
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 PowerOn 失败；未继续下载。",
                    Data = data
                };
            }
        }

        internal static StandardPlcSimActionResult Run(string apiPath)
        {
            return InvokeModeAction(apiPath, "Run");
        }

        internal static StandardPlcSimActionResult Stop(string apiPath)
        {
            return InvokeModeAction(apiPath, "Stop");
        }

        internal static StandardPlcSimActionResult ReadOutput(string apiPath, ulong byteOffset, ulong byteCount)
        {
            var data = new JsonObject
            {
                ["product"] = "SIMATIC S7-PLCSIM V16",
                ["action"] = "ReadOutput",
                ["byteOffset"] = byteOffset,
                ["byteCountRequested"] = byteCount,
                ["safety"] = new JsonObject
                {
                    ["readOnly"] = true,
                    ["startsRuntime"] = false,
                    ["createsCpu"] = false,
                    ["downloadsProgram"] = false,
                    ["changesCpuMode"] = false
                }
            };

            if (byteCount == 0 || byteCount > 4096)
            {
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "byteCount 必须在 1 到 4096 之间；未读取过程输出。",
                    Data = data
                };
            }

            var selectedPath = ResolveApiCandidates(apiPath).FirstOrDefault(File.Exists);
            data["apiAssemblyPath"] = selectedPath;
            if (selectedPath == null)
            {
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 API 未找到；未读取过程输出。",
                    Data = data
                };
            }

            try
            {
                var handle = GetController(selectedPath);
                var method = handle.ControllerType.GetMethod(
                    "ReadOutput",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: new[] { typeof(ulong), typeof(ulong) },
                    modifiers: null);
                if (method == null)
                {
                    return new StandardPlcSimActionResult
                    {
                        Ok = false,
                        Message = "SimulationController.ReadOutput(UInt64, UInt64) 未找到。",
                        Data = data
                    };
                }

                var raw = method.Invoke(handle.Controller, new object[] { byteOffset, byteCount });
                var bytes = raw as byte[] ?? Array.Empty<byte>();
                var byteNodes = new JsonArray();
                foreach (var value in bytes)
                    byteNodes.Add(value);

                data["byteCountReturned"] = bytes.Length;
                data["bytes"] = byteNodes;
                data["bytesHex"] = BitConverter.ToString(bytes).Replace("-", string.Empty);
                return new StandardPlcSimActionResult
                {
                    Ok = bytes.Length == (long)byteCount,
                    Message = "已读取标准 PLCSIM 过程输出区 " + bytes.Length + " 字节。",
                    Data = data
                };
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                data["errorType"] = root.GetType().FullName;
                data["error"] = root.Message;
                data["diagnostic"] = root.ToString();
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "读取标准 PLCSIM 过程输出失败。",
                    Data = data
                };
            }
        }

        private static StandardPlcSimActionResult InvokeModeAction(string apiPath, string action)
        {
            var data = new JsonObject
            {
                ["product"] = "SIMATIC S7-PLCSIM V16",
                ["action"] = action,
                ["safety"] = new JsonObject
                {
                    ["startsRuntime"] = false,
                    ["createsCpu"] = false,
                    ["downloadsProgram"] = false,
                    ["changesCpuMode"] = true
                }
            };

            var selectedPath = ResolveApiCandidates(apiPath).FirstOrDefault(File.Exists);
            data["apiAssemblyPath"] = selectedPath;
            if (selectedPath == null)
            {
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 API 未找到；未调用 " + action + "。",
                    Data = data
                };
            }

            try
            {
                var handle = GetController(selectedPath);
                var stateProperty = handle.ControllerType.GetProperty(
                    "State",
                    BindingFlags.Public | BindingFlags.Instance);
                var beforeState = stateProperty?.GetValue(handle.Controller, null);
                data["controllerStateBefore"] = ReadState(beforeState);

                var method = handle.ControllerType.GetMethod(
                    action,
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (method == null)
                {
                    return new StandardPlcSimActionResult
                    {
                        Ok = false,
                        Message = "SimulationController." + action + "() 未找到；未执行控制动作。",
                        Data = data
                    };
                }

                method.Invoke(handle.Controller, null);
                data["actionInvoked"] = true;

                var targetMode = string.Equals(action, "Run", StringComparison.Ordinal)
                    ? "Run"
                    : "Stop";
                var state = WaitForOperatingMode(
                    stateProperty,
                    handle.Controller,
                    targetMode,
                    TimeSpan.FromSeconds(15));
                data["controllerState"] = ReadState(state);

                var operatingMode = state?.GetType().GetProperty("OperatingMode")
                    ?.GetValue(state, null);
                var modeText = operatingMode?.ToString() ?? string.Empty;
                var modeMatched = string.Equals(modeText, targetMode, StringComparison.OrdinalIgnoreCase)
                    || (targetMode == "Run" && (modeText == "8" || modeText == "RunODIS"))
                    || (targetMode == "Stop" && modeText == "4");

                return new StandardPlcSimActionResult
                {
                    Ok = modeMatched,
                    Message = modeMatched
                        ? "标准 S7-PLCSIM V16 已切换到 " + targetMode + "。"
                        : "已调用 SimulationController." + action + "()，但未在 15 秒内确认目标 CPU 状态。",
                    Data = data
                };
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                data["actionInvoked"] = false;
                data["errorType"] = root.GetType().FullName;
                data["error"] = root.Message;
                data["diagnostic"] = root.ToString();
                return new StandardPlcSimActionResult
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 " + action + " 失败。",
                    Data = data
                };
            }
        }

        private static string[] ResolveApiCandidates(string apiPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(apiPath))
            {
                candidates.Add(Path.GetFullPath(apiPath.Trim()));
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var configuredBin = Environment.GetEnvironmentVariable("PLCSIM_V16_BIN");
            if (!string.IsNullOrWhiteSpace(configuredBin))
                candidates.Add(Path.Combine(configuredBin.Trim(), ApiAssemblyFileName));
            candidates.Add(Path.Combine(programFilesX86, "Siemens", "Automation", "PLCSIM", "V16", "Bin", ApiAssemblyFileName));
            candidates.Add(Path.Combine(programFiles, "Siemens", "Automation", "PLCSIM", "V16", "Bin", ApiAssemblyFileName));

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static ControllerHandle GetController(string apiPath)
        {
            lock (ControllerGate)
            {
                if (_controllerHandle != null
                    && string.Equals(_controllerHandle.ApiPath, apiPath, StringComparison.OrdinalIgnoreCase))
                {
                    return _controllerHandle;
                }

                var assembly = Assembly.LoadFrom(apiPath);
                var controllerType = assembly.GetType(ControllerTypeName, throwOnError: true);
                var controller = Activator.CreateInstance(controllerType);
                if (controller == null)
                    throw new InvalidOperationException("SimulationController construction returned null.");

                _controllerHandle = new ControllerHandle(apiPath, assembly, controllerType, controller);
                return _controllerHandle;
            }
        }

        private static object? WaitForPoweredOn(Type controllerType, object controller, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            object? state = null;
            do
            {
                state = controllerType.GetProperty("State", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(controller, null);
                if (ReadBooleanProperty(state, "IsPoweredOn"))
                    return state;
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);

            return state;
        }

        private static object? WaitForOperatingMode(
            PropertyInfo? stateProperty,
            object controller,
            string targetMode,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            object? state = null;
            do
            {
                state = stateProperty?.GetValue(controller, null);
                var mode = state?.GetType().GetProperty("OperatingMode")
                    ?.GetValue(state, null)?.ToString();
                var matches = string.Equals(mode, targetMode, StringComparison.OrdinalIgnoreCase)
                    || (targetMode == "Run" && (mode == "8" || mode == "RunODIS"))
                    || (targetMode == "Stop" && mode == "4");
                if (matches)
                    return state;
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);

            return state;
        }

        private static ProcessCapture CaptureProcesses(string? apiPath)
        {
            var items = new JsonArray();
            var seen = new HashSet<int>();

            foreach (var processName in StandardProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    using (process)
                    {
                        if (!seen.Add(process.Id))
                            continue;

                        string? path = null;
                        string? title = null;
                        try { path = process.MainModule?.FileName; } catch { }
                        try { title = process.MainWindowTitle; } catch { }

                        if (IsAdvancedPath(path))
                            continue;

                        items.Add(new JsonObject
                        {
                            ["processId"] = process.Id,
                            ["name"] = process.ProcessName,
                            ["path"] = path,
                            ["windowTitle"] = title,
                            ["pathMatchesResolvedApiDirectory"] = PathMatchesApiDirectory(path, apiPath)
                        });
                    }
                }
            }

            return new ProcessCapture(items, items.Count > 0);
        }

        private static bool PathMatchesApiDirectory(string? path, string? apiPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(apiPath))
                return false;

            var apiDirectory = Path.GetDirectoryName(apiPath);
            return !string.IsNullOrWhiteSpace(apiDirectory)
                && string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.Combine(apiDirectory, Path.GetFileName(path)).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdvancedPath(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && (path!.IndexOf("PLCSIMADV", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Advanced", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static JsonObject ReadState(object? state)
        {
            var result = new JsonObject();
            if (state == null)
                return result;

            foreach (var propertyName in new[]
            {
                "IsFrozen",
                "IsBooting",
                "NetworkAddress",
                "CpuName",
                "ProductDesignation",
                "IsResettingToFactoryCondition",
                "IsResettingMemory",
                "OperatingMode",
                "IsPoweredOn",
                "IsFailSafe",
                "ErrorLed",
                "MaintLed",
                "RunStopLed",
                "IsBusy",
                "SequenceState",
                "ScanDurationTimeRemainder",
                "ScanDurationCountRemainder",
                "IsBoundedScanRunning"
            })
            {
                var property = state.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null)
                    continue;

                try
                {
                    result[propertyName] = ToJsonNode(property.GetValue(state, null));
                }
                catch (Exception ex)
                {
                    result[propertyName] = "<error: " + Unwrap(ex).Message + ">";
                }
            }

            return result;
        }

        private static bool IsRuntimeReady(object? state)
        {
            if (state == null)
                return false;

            try
            {
                var poweredOn = state.GetType().GetProperty("IsPoweredOn")?.GetValue(state, null) as bool?;
                var cpuName = state.GetType().GetProperty("CpuName")?.GetValue(state, null)?.ToString();
                return poweredOn == true && !string.IsNullOrWhiteSpace(cpuName);
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadBooleanProperty(object? value, string propertyName)
        {
            if (value == null)
                return false;

            try
            {
                var property = value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                return property?.GetValue(value, null) is bool boolean && boolean;
            }
            catch
            {
                return false;
            }
        }

        private static JsonNode? ToJsonNode(object? value)
        {
            if (value == null)
                return null;
            if (value is string text)
                return JsonValue.Create(text);
            if (value is bool boolean)
                return JsonValue.Create(boolean);
            if (value is byte byteValue)
                return JsonValue.Create(byteValue);
            if (value is short shortValue)
                return JsonValue.Create(shortValue);
            if (value is int intValue)
                return JsonValue.Create(intValue);
            if (value is long longValue)
                return JsonValue.Create(longValue);
            if (value is float floatValue)
                return JsonValue.Create(floatValue);
            if (value is double doubleValue)
                return JsonValue.Create(doubleValue);
            if (value is decimal decimalValue)
                return JsonValue.Create(decimalValue);
            if (value is IDictionary dictionary)
            {
                var result = new JsonObject();
                foreach (DictionaryEntry entry in dictionary)
                    result[entry.Key?.ToString() ?? string.Empty] = ToJsonNode(entry.Value);
                return result;
            }

            return JsonValue.Create(value is Enum ? value.ToString() : Convert.ToString(value));
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        private sealed class ProcessCapture
        {
            internal ProcessCapture(JsonArray items, bool running)
            {
                Items = items;
                Running = running;
            }

            internal JsonArray Items { get; }
            internal bool Running { get; }
        }

        private sealed class ControllerHandle
        {
            internal ControllerHandle(string apiPath, Assembly assembly, Type controllerType, object controller)
            {
                ApiPath = apiPath;
                Assembly = assembly;
                ControllerType = controllerType;
                Controller = controller;
            }

            internal string ApiPath { get; }
            internal Assembly Assembly { get; }
            internal Type ControllerType { get; }
            internal object Controller { get; }
        }
    }

    internal sealed class StandardPlcSimProbeResult
    {
        internal bool Ok { get; set; }
        internal string Message { get; set; } = string.Empty;
        internal JsonObject Data { get; set; } = new JsonObject();
    }

    internal sealed class StandardPlcSimActionResult
    {
        internal bool Ok { get; set; }
        internal string Message { get; set; } = string.Empty;
        internal JsonObject Data { get; set; } = new JsonObject();
    }
}
