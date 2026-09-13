using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Runtime;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        [McpServerTool(Name = "ProbeStandardPlcSim"), Description("[L1][PLCSIM-V16] Read-only environment probe for the standard SIMATIC S7-PLCSIM V16 API. Checks the installed V16 API assembly, constructs its SimulationController, reads the initial controller state, and reports standard-process evidence. PLCSIM Advanced is explicitly excluded. Does not start PLCSIM, create a simulation CPU, download a program, change CPU mode, or read project tags.")]
        public static ResponseJsonReport ProbeStandardPlcSim(
            [Description("apiPath: optional full path to Siemens.Simatic.PlcSim.VplcApi.dll. Empty uses the known V16 installation candidates.")] string apiPath = "")
        {
            try
            {
                var result = StandardPlcSimV16.Probe(apiPath);
                return new ResponseJsonReport
                {
                    Ok = result.Ok,
                    Message = result.Message,
                    Data = result.Data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = result.Ok,
                        ["evidenceLevel"] = "static",
                        ["runtimeControlPerformed"] = false
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 探测失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString(),
                        ["safety"] = new JsonObject
                        {
                            ["readOnly"] = true,
                            ["runtimeControlPerformed"] = false
                        }
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "static"
                    }
                };
            }
        }

#if TIA_V16
        [McpServerTool(Name = "SetSimulationDuringBlockCompilation"), Description("[L1][TIA-V16][PROJECT-WRITE] Enable or disable the project-level 'Support simulation during block compilation' setting and save the currently open project. Requires Connect + OpenProject. Use before CompileSoftware when downloading the isolated project to standard PLCSIM.")]
        public static ResponseJsonReport SetSimulationDuringBlockCompilation(
            [Description("enabled: true enables project support for simulation during block compilation; false restores the project setting.")] bool enabled = true)
        {
            try
            {
                return Portal.SetSimulationDuringBlockCompilation(enabled);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "设置项目仿真编译属性失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString(),
                        ["enabledRequested"] = enabled,
                        ["readBack"] = false
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "portal-runtime"
                    }
                };
            }
        }

        [McpServerTool(Name = "InspectDownloadRoutes"), Description("[L1][PLCSIM-V16][ROUTE-BINDING] Enumerate the current PLC download configuration through Modes -> PcInterfaces -> TargetInterfaces and optionally apply a PLCSIM or Softbus target. Requires Connect + OpenProject. This tool does not download a program or change CPU RUN/STOP state.")]
        public static ResponseJsonReport InspectDownloadRoutes(
            [Description("softwarePath: exact PLC software path from the open project, normally 'PLC_1'.")] string softwarePath = "PLC_1",
            [Description("preferredInterface: route token, normally 'PLCSIM'; use 'Softbus' as fallback.")] string preferredInterface = "PLCSIM",
            [Description("applyConfiguration: true applies the selected target to the current download configuration; false only enumerates and selects.")] bool applyConfiguration = true)
        {
            try
            {
                return Portal.InspectDownloadRoutes(softwarePath, preferredInterface, applyConfiguration);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "标准 PLCSIM 下载路由探测失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString(),
                        ["routeApplied"] = false
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "portal-runtime",
                        ["routeApplied"] = false
                    }
                };
            }
        }

        [McpServerTool(Name = "PowerOnStandardPlcSim"), Description("[L1][PLCSIM-V16][RUNTIME-WRITE] Call the standard SIMATIC S7-PLCSIM V16 SimulationController.PowerOn for an S7-1200 simulation and wait for the powered-on standby state. This starts the standard simulation runtime and creates or opens its isolated runtime storage; it does not download a program or change the CPU to RUN. Use before DownloadToStandardPlcSim.")]
        public static ResponseJsonReport PowerOnStandardPlcSim(
            [Description("storageDirectory: absolute isolated directory used by the V16 PLCSIM runtime for this simulation instance.")] string storageDirectory,
            [Description("apiPath: optional full path to Siemens.Simatic.PlcSim.VplcApi.dll. Empty uses the known V16 installation candidates.")] string apiPath = "")
        {
            try
            {
                var result = StandardPlcSimV16.PowerOn(apiPath, storageDirectory);
                return new ResponseJsonReport
                {
                    Ok = result.Ok,
                    Message = result.Message,
                    Data = result.Data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = result.Ok,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 PowerOn 调用失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString(),
                        ["safety"] = new JsonObject
                        {
                            ["startsRuntime"] = true,
                            ["downloadsProgram"] = false,
                            ["changesCpuMode"] = false
                        }
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = true
                    }
                };
            }
        }

        [McpServerTool(Name = "RunStandardPlcSim"), Description("[L1][PLCSIM-V16][RUNTIME-WRITE] Call SimulationController.Run on the cached standard S7-PLCSIM V16 controller and wait up to 15 seconds for OperatingMode=Run. Requires a prior PowerOnStandardPlcSim in the same MCP process; it does not download a program.")]
        public static ResponseJsonReport RunStandardPlcSim(
            [Description("apiPath: optional full path to Siemens.Simatic.PlcSim.VplcApi.dll. Empty uses the known V16 installation candidates.")] string apiPath = "")
        {
            try
            {
                var result = StandardPlcSimV16.Run(apiPath);
                return new ResponseJsonReport
                {
                    Ok = result.Ok,
                    Message = result.Message,
                    Data = result.Data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = result.Ok,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 RUN 调用失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString()
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = true
                    }
                };
            }
        }

        [McpServerTool(Name = "StopStandardPlcSim"), Description("[L1][PLCSIM-V16][RUNTIME-WRITE] Call SimulationController.Stop on the cached standard S7-PLCSIM V16 controller and wait up to 15 seconds for OperatingMode=Stop. Requires a prior PowerOnStandardPlcSim in the same MCP process.")]
        public static ResponseJsonReport StopStandardPlcSim(
            [Description("apiPath: optional full path to Siemens.Simatic.PlcSim.VplcApi.dll. Empty uses the known V16 installation candidates.")] string apiPath = "")
        {
            try
            {
                var result = StandardPlcSimV16.Stop(apiPath);
                return new ResponseJsonReport
                {
                    Ok = result.Ok,
                    Message = result.Message,
                    Data = result.Data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = result.Ok,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "标准 S7-PLCSIM V16 STOP 调用失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString()
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = true
                    }
                };
            }
        }

        [McpServerTool(Name = "ReadStandardPlcSimOutput"), Description("[L1][PLCSIM-V16][RUNTIME-READ] Read bytes from the standard S7-PLCSIM V16 process-output image through SimulationController.ReadOutput. This is a read-only runtime channel; it does not read optimized DB members or change CPU mode. Requires a prior PowerOnStandardPlcSim in the same MCP process.")]
        public static ResponseJsonReport ReadStandardPlcSimOutput(
            [Description("byteOffset: zero-based offset in the simulated process-output image.")] ulong byteOffset = 0,
            [Description("byteCount: number of bytes to read, from 1 through 4096.")] ulong byteCount = 16,
            [Description("apiPath: optional full path to Siemens.Simatic.PlcSim.VplcApi.dll. Empty uses the known V16 installation candidates.")] string apiPath = "")
        {
            try
            {
                var result = StandardPlcSimV16.ReadOutput(apiPath, byteOffset, byteCount);
                return new ResponseJsonReport
                {
                    Ok = result.Ok,
                    Message = result.Message,
                    Data = result.Data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = result.Ok,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = false
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseJsonReport
                {
                    Ok = false,
                    Message = "读取标准 S7-PLCSIM 过程输出失败：" + ex.Message,
                    Data = new JsonObject
                    {
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.ToString()
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = false,
                        ["evidenceLevel"] = "simulation-runtime",
                        ["runtimeControlPerformed"] = false
                    }
                };
            }
        }

        [McpServerTool(Name = "DownloadToStandardPlcSim"), Description("[L1][PLCSIM-V16][RUNTIME-WRITE] Bind the selected PLCSIM/Softbus route and download the compiled PLC hardware and software from the currently open isolated TIA Portal V16 project. Requires Connect + OpenProject + CompileSoftware + CheckDownloadReadiness; standard PLCSIM runtime readiness must be established separately. Defaults to leave the simulated CPU stopped after download so RUN can be verified separately.")]
        public static ResponseDownload DownloadToStandardPlcSim(
            [Description("softwarePath: exact PLC software path from GetProjectTree, normally 'PLC_1'.")] string softwarePath = "PLC_1",
            [Description("consistentBlocksOnly: true=download only consistent blocks.")] bool consistentBlocksOnly = true,
            [Description("keepActualValues: true=preserve current DB actual values.")] bool keepActualValues = true,
            [Description("startAfterDownload: false=leave the simulated CPU in STOP/standby for a separate RUN check.")] bool startAfterDownload = false,
            [Description("stopBeforeDownload: true=stop the simulated CPU before download.")] bool stopBeforeDownload = true,
            [Description("password: optional CPU download password.")] string password = "",
            [Description("preferredInterface: route token, normally 'PLCSIM'; use 'Softbus' as fallback.")] string preferredInterface = "PLCSIM",
            [Description("includeHardware: true=download hardware and software together, required for a fresh simulation target.")] bool includeHardware = true)
        {
            try
            {
                var route = Portal.InspectDownloadRoutes(softwarePath, preferredInterface, true);
                if (route.Ok != true || route.Data?["routeApplied"]?.GetValue<bool>() != true)
                {
                    return new ResponseDownload
                    {
                        Ok = false,
                        Message = $"标准 PLCSIM 下载路由未确认，未执行下载：{route.Message}",
                        Errors = route.Data?["applyError"] == null
                            ? new[] { route.Message ?? "Unknown route binding failure." }
                            : new[] { route.Data["applyError"]!.ToString() }
                    };
                }

                var result = Portal.DownloadToPlc(
                    softwarePath,
                    consistentBlocksOnly,
                    keepActualValues,
                    startAfterDownload,
                    stopBeforeDownload,
                    string.IsNullOrWhiteSpace(password) ? null : password,
                    preferredInterface,
                    includeHardware);
                result.Message = $"标准 PLCSIM 下载调用完成：{result.Message}";
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                return new ResponseDownload
                {
                    Ok = false,
                    Message = "下载到标准 S7-PLCSIM V16 失败：" + ex.Message,
                    Errors = new[] { ex.ToString() }
                };
            }
        }
#endif
    }
}
