using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Addressing;
using Microsoft.Win32;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Reads an adapter's IPv4 configuration (BCL plus the read-only <c>NameServer</c> registry value for
    /// static DNS) and changes it with <c>netsh interface ipv4</c>, judged by exit code only
    /// (research D1/D2). Nothing is written to the registry by this program.
    /// </summary>
    internal sealed class NetshAdapterConfigService : IAdapterConfigService
    {
        private const int CommandTimeoutMilliseconds = 10000;
        private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        public AdapterConfig Read(string adapterId)
        {
            var ni = Find(adapterId);
            if (ni == null)
            {
                return null;
            }

            try
            {
                var props = ni.GetIPProperties();
                var v4 = props.GetIPv4Properties();

                var addresses = props.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => new AdapterAddress(u.Address, u.PrefixLength))
                    .ToList();
                var gateway = props.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                var dns = ReadStaticDns(adapterId);

                return new AdapterConfig(
                    adapterId,
                    v4.Index,
                    addresses,
                    gateway,
                    dns,
                    v4.IsDhcpEnabled ? AddressMode.Automatic : AddressMode.Static,
                    dns.Count > 0 ? DnsMode.Static : DnsMode.Automatic);
            }
            catch (NetworkInformationException)
            {
                // No IPv4 binding on this adapter: nothing this program can configure.
                return null;
            }
        }

        public StepResult Execute(ApplyStep step, AdapterConfig target, int addressIndex = 0)
        {
            var ni = Find(target.AdapterId);
            if (ni == null)
            {
                return StepResult.Fail(step, -2);
            }

            int index;
            try
            {
                index = ni.GetIPProperties().GetIPv4Properties().Index;
            }
            catch (NetworkInformationException)
            {
                return StepResult.Fail(step, -2);
            }

            foreach (var arguments in NetshCommands.Build(step, target, addressIndex, index))
            {
                var exitCode = RunNetsh(arguments);
                if (exitCode != 0)
                {
                    return StepResult.Fail(step, exitCode);
                }
            }

            return StepResult.Ok(step);
        }

        private static NetworkInterface Find(string adapterId)
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == adapterId);
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        private static IReadOnlyList<IPAddress> ReadStaticDns(string adapterId)
        {
            var result = new List<IPAddress>();
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(InterfacesKey + adapterId, false))
                {
                    var text = key?.GetValue("NameServer") as string;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        foreach (var part in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            IPAddress ip;
                            if (IPAddress.TryParse(part, out ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                            {
                                result.Add(ip);
                            }
                        }
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is IOException || e is UnauthorizedAccessException)
            {
                // Unreadable: treat as "no static DNS"; the verification step will notice a real difference.
            }

            return result;
        }

        private static int RunNetsh(string arguments)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(start))
                {
                    // The text is localized and never used; drain it so the process cannot block on a full pipe.
                    process.OutputDataReceived += (s, e) => { };
                    process.ErrorDataReceived += (s, e) => { };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(CommandTimeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        return -1;
                    }

                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return -3;
            }
        }
    }
}
