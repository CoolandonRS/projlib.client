using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using netlib;

namespace projupdate_client; 

public static class Negotiator {
    /// <summary>
    /// The mode for the Negotiation
    /// </summary>
    public enum Mode {
        Update, Download, Console, ListProjects
    }

    /// <summary>
    /// SafeNegotiate, but throws instead of NegotiationResult failure
    /// </summary>
    /// <param name="connection">The server connection</param>
    /// <param name="mode">The mode to run in</param>
    /// <param name="projVer">The version of a program to be <see cref="Mode.Update">Mode.Update</see>. Is not used in other modes.</param>
    /// <param name="prompt">How to get missing data. If null, it will throw an UpdateClientException</param>
    /// <param name="log">How to log messages. Can be nothing, Console.WriteLine, or whatever else.</param>
    /// <returns>In ListAll mode, a list of all programs. In download mode, the binary. In update mode, the base64 of the binary if out of date. Unspecified return success/failure</returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="ServerAuthException"></exception>
    public static NegotiationResult RawNegotiate(ServerConnection connection, Mode mode, SemVer? projVer = null, Func<string, string>? prompt = null, Action<string>? log = null) {
        var communicator = connection.GetCommunicator();
        switch (mode) {
            default: throw new InvalidEnumArgumentException();
            case Mode.Update:
                projVer ??= new SemVer(Prompt("Program version in SemVer format: ", prompt));
                communicator.WriteStr("version");
                var serVer = new SemVer(RegExtract(communicator.ReadStr(), "[0-9]+\\.[0-9]+\\.[0-9]+"));
                switch (projVer.CompareTo(serVer).comp) {
                    case SemVer.Comparison.Beta:
                        log?.Invoke("WARN: You are currently running a beta version!");
                        return new NegotiationResult(true);
                    case SemVer.Comparison.Current:
                        return new NegotiationResult(true);
                    case SemVer.Comparison.Outdated:
                        Console.WriteLine("Program is outdated. Updating...");
                        break;
                    default:
                        throw new InvalidEnumArgumentException();
                }
                goto case Mode.Download;
            case Mode.Download:
                communicator.WriteStr("sha256sum");
                var serverSum = Extract(communicator.ReadStr());
                communicator.WriteStr("len");
                var len = int.Parse(Extract(communicator.ReadStr()));
                communicator.WriteStr("binary");
                if (!NetUtil.IsAck(communicator.ReadStr())) throw new ServerOperationException("Server failed to ack request");
                var bin = communicator.GetRSAkeys().recieve.Decrypt(communicator.ReadRawN(len));
                var clientSum = NetUtil.GetSha256Sum(bin);
                if (serverSum != clientSum) throw new ServerOperationException("sha256sums do not match");
                return new NegotiationResult(bin);
            case Mode.Console:
                if (!connection.IsDevMode()) throw new InvalidOperationException("Non dev mode console connection attempted");
                var cmd = "";
                while (cmd != "disconnect") {
                    cmd = Console.ReadLine()!;
                    if (new[] { "sha256sum", "len", "truelen", "binary" }.Contains(cmd)) {
                        log?.Invoke("NAK: Unsupported in dev mode");
                        continue;
                    }
                    Console.WriteLine(communicator.ReadStr());
                }
                return new NegotiationResult(true);
        }
    }

    /// <summary>
    /// RawNegotiate, but NegotiationResult failure instead of throwing
    /// </summary>
    /// <param name="connection">The server connection</param>
    /// <param name="mode">The mode to run in</param>
    /// <param name="projVer">The version of a program to be used in <see cref="Mode.Update">Mode.Update</see>. Is not used in other modes.</param>
    /// <returns>In ListAll mode, a list of all programs. In download mode, the binary. In update mode, the base64 of the binary if out of date. Unspecified return success/failure</returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="ServerAuthException"></exception>
    public static NegotiationResult SafeNegotiate(ServerConnection connection, Mode mode, SemVer? projVer = null) {
        try {
            return RawNegotiate(connection, mode, projVer);
        } catch {
            return new NegotiationResult(false);
        }
    }


    private static string RegExtract(string str, string regex, string noack = "Server failed to ack request") {
        var match = Regex.Match(Extract(str, noack), regex);
        if (!match.Success) throw new InvalidOperationException("No match");
        return match.Value;
    }

    private static string Extract(string str, string noack = "Server failed to ack request") {
        if (!NetUtil.IsAck(str)) throw new ServerOperationException(noack);
        return str.Replace("ACK: ", "");
    }

    private static string Prompt(string prompt, Func<string, string>? func) {
        if (func == null) throw new UpdateClientException("Prompted without a prompt function specified");
        return func(prompt);
    }
}