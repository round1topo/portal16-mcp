using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Siemens.Engineering.Download;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        private sealed class DownloadRouteCandidate
        {
            public string ModeName { get; set; } = string.Empty;
            public string PcInterfaceName { get; set; } = string.Empty;
            public string TargetInterfaceName { get; set; } = string.Empty;
            public object TargetInterface { get; set; } = null!;

            public string Label =>
                $"{ModeName}/{PcInterfaceName}/{TargetInterfaceName}";
        }

        /// <summary>
        /// Enumerates the download routes exposed by the current PLC software and optionally
        /// applies one explicit PLCSIM/Softbus target. This is intentionally separate from
        /// DownloadToPlc so route selection can be verified before any download side effect.
        /// </summary>
        public ResponseJsonReport InspectDownloadRoutes(
            string softwarePath,
            string preferredInterface = "PLCSIM",
            bool applyConfiguration = true)
        {
            return _sta.Run(() =>
            {
                var data = new JsonObject
                {
                    ["softwarePath"] = softwarePath,
                    ["preferredInterface"] = preferredInterface ?? string.Empty,
                    ["applyConfigurationRequested"] = applyConfiguration,
                    ["routeApplied"] = false
                };

                if (IsProjectNull())
                {
                    return new ResponseJsonReport
                    {
                        Ok = false,
                        Message = "No project open.",
                        Data = data
                    };
                }

                var plcSoftware = GetPlcSoftware(softwarePath);
                if (plcSoftware == null)
                {
                    return new ResponseJsonReport
                    {
                        Ok = false,
                        Message = $"PLC software not found: '{softwarePath}'.",
                        Data = data
                    };
                }

                try
                {
                    var provider = ResolvePlcService<DownloadProvider>(softwarePath, plcSoftware);
                    if (provider == null)
                    {
                        return new ResponseJsonReport
                        {
                            Ok = false,
                            Message = "DownloadProvider service not available for this PLC.",
                            Data = data
                        };
                    }

                    var configuration = provider.Configuration;
                    if (configuration == null)
                    {
                        return new ResponseJsonReport
                        {
                            Ok = false,
                            Message = "No download connection configuration found.",
                            Data = data
                        };
                    }

                    var allRoutes = EnumerateDownloadRoutes(configuration);
                    var routeArray = new JsonArray();
                    foreach (var route in allRoutes)
                        routeArray.Add(ToRouteJson(route));
                    data["availableRoutes"] = routeArray;
                    data["routeCount"] = allRoutes.Count;

                    var selected = SelectSimulationRoute(allRoutes, preferredInterface);
                    if (selected == null)
                    {
                        data["availableSimulationRoutes"] = new JsonArray(
                            allRoutes
                                .Where(IsSimulationRoute)
                                .Select(ToRouteJson)
                                .ToArray());

                        return new ResponseJsonReport
                        {
                            Ok = false,
                            Message = "No PLCSIM or Softbus target interface matched the requested route.",
                            Data = data,
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["evidenceLevel"] = "portal-runtime",
                                ["routeApplied"] = false
                            }
                        };
                    }

                    data["selectedRoute"] = ToRouteJson(selected);
                    data["applyMethod"] = FindApplyConfigurationMethod(configuration)?.ToString() ?? string.Empty;

                    if (applyConfiguration)
                    {
                        if (!TryApplyDownloadRoute(configuration, selected, out _, out var applyError))
                        {
                            data["applyError"] = applyError;
                            return new ResponseJsonReport
                            {
                                Ok = false,
                                Message = $"Failed to apply download route '{selected.Label}'.",
                                Data = data,
                                Meta = new JsonObject
                                {
                                    ["timestamp"] = DateTime.Now,
                                    ["evidenceLevel"] = "portal-runtime",
                                    ["routeApplied"] = false
                                }
                            };
                        }

                        data["routeApplied"] = true;
                    }

                    return new ResponseJsonReport
                    {
                        Ok = true,
                        Message = applyConfiguration
                            ? $"Download route applied: {selected.Label}"
                            : $"Download routes enumerated; selected candidate: {selected.Label}",
                        Data = data,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["evidenceLevel"] = "portal-runtime",
                            ["routeApplied"] = applyConfiguration
                        }
                    };
                }
                catch (Exception ex)
                {
                    data["errorType"] = ex.GetType().FullName ?? ex.GetType().Name;
                    data["error"] = ex.ToString();
                    return new ResponseJsonReport
                    {
                        Ok = false,
                        Message = $"InspectDownloadRoutes failed: {ex.Message}",
                        Data = data,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["evidenceLevel"] = "portal-runtime",
                            ["routeApplied"] = false
                        }
                    };
                }
            });
        }

        private static List<DownloadRouteCandidate> EnumerateDownloadRoutes(object configuration)
        {
            var routes = new List<DownloadRouteCandidate>();
            foreach (var mode in EnumerateProperty(configuration, "Modes"))
            {
                var modeName = ReadName(mode);
                foreach (var pcInterface in EnumerateProperty(mode, "PcInterfaces"))
                {
                    var pcInterfaceName = ReadName(pcInterface);
                    foreach (var target in EnumerateProperty(pcInterface, "TargetInterfaces"))
                    {
                        if (target == null) continue;
                        routes.Add(new DownloadRouteCandidate
                        {
                            ModeName = modeName,
                            PcInterfaceName = pcInterfaceName,
                            TargetInterfaceName = ReadName(target),
                            TargetInterface = target
                        });
                    }
                }
            }

            return routes;
        }

        private static DownloadRouteCandidate? SelectSimulationRoute(
            IEnumerable<DownloadRouteCandidate> routes,
            string? preferredInterface)
        {
            var simulationRoutes = routes.Where(IsSimulationRoute).ToList();
            if (simulationRoutes.Count == 0) return null;

            var requested = string.IsNullOrWhiteSpace(preferredInterface)
                ? new[] { "PLCSIM", "Softbus" }
                : new[] { preferredInterface! };

            foreach (var token in requested)
            {
                var selected = simulationRoutes.FirstOrDefault(route =>
                    ContainsIgnoreCase(route.ModeName, token)
                    || ContainsIgnoreCase(route.PcInterfaceName, token)
                    || ContainsIgnoreCase(route.TargetInterfaceName, token));
                if (selected != null) return selected;
            }

            return null;
        }

        private static bool IsSimulationRoute(DownloadRouteCandidate route)
        {
            return ContainsIgnoreCase(route.ModeName, "PLCSIM")
                || ContainsIgnoreCase(route.ModeName, "Softbus")
                || ContainsIgnoreCase(route.PcInterfaceName, "PLCSIM")
                || ContainsIgnoreCase(route.PcInterfaceName, "Softbus")
                || ContainsIgnoreCase(route.TargetInterfaceName, "PLCSIM")
                || ContainsIgnoreCase(route.TargetInterfaceName, "Softbus");
        }

        private static bool TryApplyDownloadRoute(
            object configuration,
            DownloadRouteCandidate route,
            out object? appliedConfiguration,
            out string error)
        {
            appliedConfiguration = configuration;
            error = string.Empty;

            var method = FindApplyConfigurationMethod(configuration);
            if (method == null)
            {
                error = "ApplyConfiguration(ConfigurationTargetInterface) is not available.";
                return false;
            }

            try
            {
                var result = method.Invoke(configuration, new[] { route.TargetInterface });
                if (result is bool applied)
                {
                    if (!applied)
                    {
                        error = "ApplyConfiguration returned false.";
                        return false;
                    }

                    // V16 returns Boolean and applies the target in place. Newer
                    // bindings may return an IConfiguration instance instead.
                    appliedConfiguration = configuration;
                }
                else
                {
                    appliedConfiguration = result ?? configuration;
                }
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                error = ex.InnerException.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static MethodInfo? FindApplyConfigurationMethod(object configuration)
        {
            return configuration.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "ApplyConfiguration", StringComparison.Ordinal))
                        return false;
                    var parameters = method.GetParameters();
                    return parameters.Length == 1
                        && string.Equals(
                            parameters[0].ParameterType.Name,
                            "ConfigurationTargetInterface",
                            StringComparison.Ordinal);
                });
        }

        private static JsonObject ToRouteJson(DownloadRouteCandidate route)
        {
            return new JsonObject
            {
                ["mode"] = route.ModeName,
                ["pcInterface"] = route.PcInterfaceName,
                ["targetInterface"] = route.TargetInterfaceName,
                ["label"] = route.Label,
                ["simulationRoute"] = IsSimulationRoute(route)
            };
        }

        private static IEnumerable<object?> EnumerateProperty(object? owner, string propertyName)
        {
            if (owner == null) yield break;

            object? value;
            try
            {
                value = owner.GetType().GetProperty(propertyName)?.GetValue(owner);
            }
            catch
            {
                yield break;
            }

            if (value is not IEnumerable items || value is string) yield break;
            foreach (var item in items) yield return item;
        }

        private static string ReadName(object? value)
        {
            if (value == null) return string.Empty;
            try
            {
                return value.GetType().GetProperty("Name")?.GetValue(value)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ContainsIgnoreCase(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
