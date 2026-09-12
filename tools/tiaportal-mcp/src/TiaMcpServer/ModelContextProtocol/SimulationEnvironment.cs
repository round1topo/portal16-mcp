using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        [McpServerTool(Name = "DetectPlcSimulationEnvironment"), Description(
            "[L1][PLCSIM] Read-only environment probe for standard SIMATIC S7-PLCSIM and PLCSIM Advanced. " +
            "Checks installed product/path evidence, running processes, and network adapters. " +
            "Does not start PLCSIM, create instances, download, or change CPU state. " +
            "Use this before implementing or invoking simulation lifecycle operations.")]
        public static ResponseSimulationEnvironment DetectPlcSimulationEnvironment()
        {
            var snapshot = PlcSimulationEnvironmentProbe.Capture();
            return new ResponseSimulationEnvironment
            {
                Ok = true,
                Message = "PLC simulation environment probe completed; this is detection evidence only.",
                StandardPlcSimDetected = snapshot.StandardPlcSimDetected,
                PlcSimAdvancedDetected = snapshot.PlcSimAdvancedDetected,
                StandardPlcSimRunning = snapshot.StandardPlcSimRunning,
                PlcSimAdvancedRunning = snapshot.PlcSimAdvancedRunning,
                Evidence = snapshot.Evidence.ToArray(),
                Processes = snapshot.Processes.ToArray(),
                NetworkAdapters = snapshot.NetworkAdapters.ToArray(),
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["simulationLifecycleImplemented"] = false,
                    ["readOnly"] = true
                }
            };
        }
    }

    internal static class PlcSimulationEnvironmentProbe
    {
        private static readonly string[] StandardProcessNames =
        {
            "s7plcsim", "plcsim", "plcsimv16", "simaticplcsim"
        };

        private static readonly string[] AdvancedProcessNames =
        {
            "plcsimadv", "plcsimadvanced", "simaticplcsimadvanced"
        };

        public static PlcSimulationEnvironmentSnapshot Capture()
        {
            var processes = ReadProcesses();
            var evidence = new List<string>();
            bool standardInstalled = false;
            bool advancedInstalled = false;

            AddRegistryEvidence(evidence, ref standardInstalled, ref advancedInstalled);
            AddPathEvidence(evidence, ref standardInstalled, ref advancedInstalled);

            bool standardRunning = processes.Any(p => p.Product == "PLCSIM");
            bool advancedRunning = processes.Any(p => p.Product == "PLCSIM Advanced");
            if (standardRunning) standardInstalled = true;
            if (advancedRunning) advancedInstalled = true;

            return new PlcSimulationEnvironmentSnapshot
            {
                StandardPlcSimDetected = standardInstalled,
                PlcSimAdvancedDetected = advancedInstalled,
                StandardPlcSimRunning = standardRunning,
                PlcSimAdvancedRunning = advancedRunning,
                Evidence = evidence,
                Processes = processes,
                NetworkAdapters = ReadNetworkAdapters()
            };
        }

        private static List<PlcSimulationProcess> ReadProcesses()
        {
            var rows = new List<PlcSimulationProcess>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    string name = process.ProcessName ?? string.Empty;
                    string lower = name.ToLowerInvariant();
                    bool standard = StandardProcessNames.Any(n => lower.Contains(n));
                    bool advanced = AdvancedProcessNames.Any(n => lower.Contains(n));
                    if (!standard && !advanced) continue;

                    rows.Add(new PlcSimulationProcess
                    {
                        ProcessId = process.Id,
                        Name = name,
                        Product = advanced ? "PLCSIM Advanced" : "PLCSIM"
                    });
                }
                catch
                {
                    // A process can exit between enumeration and property access.
                }
                finally
                {
                    process.Dispose();
                }
            }
            return rows;
        }

        private static void AddRegistryEvidence(
            List<string> evidence,
            ref bool standardInstalled,
            ref bool advancedInstalled)
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var rootPath in roots)
            {
                using (var root = Registry.LocalMachine.OpenSubKey(rootPath))
                {
                    if (root == null) continue;
                    foreach (var keyName in root.GetSubKeyNames())
                    {
                        using (var key = root.OpenSubKey(keyName))
                        {
                            string displayName = key?.GetValue("DisplayName") as string ?? string.Empty;
                            if (displayName.IndexOf("PLCSIM Advanced", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                advancedInstalled = true;
                                evidence.Add("registry: " + displayName);
                            }
                            else if (displayName.IndexOf("PLCSIM", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                standardInstalled = true;
                                evidence.Add("registry: " + displayName);
                            }
                        }
                    }
                }
            }
        }

        private static void AddPathEvidence(
            List<string> evidence,
            ref bool standardInstalled,
            ref bool advancedInstalled)
        {
            string[] candidates =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };

            foreach (string root in candidates.Where(Directory.Exists))
            {
                try
                {
                    foreach (string path in Directory.EnumerateDirectories(root, "*PLCSIM*", SearchOption.TopDirectoryOnly))
                    {
                        bool advanced = path.IndexOf("Advanced", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (advanced) advancedInstalled = true;
                        else standardInstalled = true;
                        evidence.Add("path: " + path);
                    }
                }
                catch
                {
                    // Installation folders can be inaccessible; registry evidence remains useful.
                }
            }
        }

        private static List<PlcSimulationNetworkAdapter> ReadNetworkAdapters()
        {
            var rows = new List<PlcSimulationNetworkAdapter>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    var addresses = adapter.GetIPProperties().UnicastAddresses
                        .Select(a => a.Address.ToString())
                        .ToArray();
                    rows.Add(new PlcSimulationNetworkAdapter
                    {
                        Name = adapter.Name,
                        Description = adapter.Description,
                        Type = adapter.NetworkInterfaceType.ToString(),
                        OperationalStatus = adapter.OperationalStatus.ToString(),
                        Addresses = addresses
                    });
                }
                catch
                {
                    // Keep the probe best-effort if an adapter disappears during enumeration.
                }
            }
            return rows;
        }
    }

    internal sealed class PlcSimulationEnvironmentSnapshot
    {
        public bool StandardPlcSimDetected { get; set; }
        public bool PlcSimAdvancedDetected { get; set; }
        public bool StandardPlcSimRunning { get; set; }
        public bool PlcSimAdvancedRunning { get; set; }
        public List<string> Evidence { get; set; } = new List<string>();
        public List<PlcSimulationProcess> Processes { get; set; } = new List<PlcSimulationProcess>();
        public List<PlcSimulationNetworkAdapter> NetworkAdapters { get; set; } = new List<PlcSimulationNetworkAdapter>();
    }
}
