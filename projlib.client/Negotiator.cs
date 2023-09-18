using System.ComponentModel;
using System.Text.RegularExpressions;
using CoolandonRS.keyring.Yubikey;
using CoolandonRS.netlib;
using CoolandonRS.projlib.client.generics;

namespace CoolandonRS.projlib.client; 

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
    /// <returns>ListAll mode will throw. In download mode, the binary. In update mode, the binary if outdated. Unspecified return success/failure</returns>
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
                        log?.Invoke("Program is outdated. Updating...");
                        break;
                    default:
                        throw new InvalidEnumArgumentException();
                }
                goto case Mode.Download;
            case Mode.Download:
                communicator.WriteStr("sha256sum");
                var serverSum = Extract(communicator.ReadStr());
                communicator.WriteStr("binary");
                if (!NetUtil.IsAck(communicator.ReadStr())) throw new ServerOperationException("Server failed to ack request");
                var bin = communicator.Read();
                var clientSum = NetUtil.GetSha256Sum(bin);
                if (serverSum != clientSum) throw new DiscrepancyException("checksum mismatch");
                return new NegotiationResult(bin);
            case Mode.Console:
                if (!connection.IsDevMode()) log?.Invoke("WARN: Opened console in project mode. Did you mean to do this?");
                var cmd = "";
                while (cmd != "disconnect") {
                    cmd = Prompt("> ", prompt);
                    log?.Invoke(communicator.ReadStr());
                }
                return new NegotiationResult(true);
            case Mode.ListProjects:
                throw new InvalidOperationException();
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

    /// <summary>
    /// Utility method that does all of the updating stuff for you<br/>
    /// </summary>
    /// <param name="project">Tuple of your project nae and version</param>
    /// <param name="client">Tuple of the users identifier and public PEM</param>
    /// <param name="log">An optional function to log messages</param>
    /// <param name="suppressExceptions">Whether or not to catch all errors, returning false instead.</param>
    public static bool Update((string name, SemVer ver) project, (string name, string pem) client, Action<string> log = null, bool suppressExceptions = false) {
        try {
            var con = new ServerConnection();
            con.Init(client.name, client.pem, project.name);
            RawNegotiate(con, Mode.Update, project.ver, null, log);
        } catch {
            if (suppressExceptions) return false;
            throw;
        }
        return true;
    }

    public static string ListAll((string name, string pem) client) {
        return new ServerConnection().InitAndListAll(client.name, client.pem);
    }

    public static bool Console((string name, string pem) client, Func<string, string> prompt, Action<string> log, bool suppressExceptions = false) {
        try {
            var con = new ServerConnection();
            con.InitDev(client.name, client.pem);
            RawNegotiate(con, Mode.Console, null, prompt, log);
        } catch {
            if (suppressExceptions) return false;
            throw;
        }
        return true;
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