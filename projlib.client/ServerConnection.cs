﻿using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using netlib;

namespace projupdate_client; 

public class ServerConnection {
    public static readonly SemVer UpdaterVer = new SemVer(1, 0, 0);
    private static readonly string[] KnownUpdaters = { "eidolon", "updater.numra.net", "elfib" };
    private readonly TcpClient client;
    private TcpRsaCommunicator communicator;
    private const int UpdaterPort = 1248;
    private const string ServerPem = "";
    private bool active;
    private bool devMode = false;
    private object threadSafety = new();


    /// <summary>
    /// Tries to find an available server and connects to its socket
    /// </summary>
    /// <returns>The connected socket, or null if no server is found</returns>
    public static TcpClient? TryFindServer() {
        var addr = KnownUpdaters.FirstOrDefault(CanPing);
        if (addr == null) return null;
        return new TcpClient(addr, UpdaterPort);
    }

    /// <summary>
    /// Finds an available server and connects to its socket
    /// </summary>
    /// <returns></returns>
    /// <exception cref="ServerConnectionException"></exception>
    public static TcpClient FindServer() {
        var server = TryFindServer();
        if (server == null) throw new ServerConnectionException("No server found");
        return server;
    }

    private static bool CanPing(string addr) {
        try {
            using var pinger = new Ping();
            return pinger.Send(addr).Status == IPStatus.Success;
        } catch {
            return false;
        }
    }

    public string InitAndListAll(string clientName, string clientPem) {
        GenericLogin(clientName, clientPem);
        communicator.WriteStr("listAll");
        if (!NetUtil.IsAck(communicator.ReadStr())) throw new ServerOperationException("Server failed to ack request");
        return communicator.ReadStr();
    }

    public void Init(string clientName, string clientPem, string projName, string? overwritePlatform = null) {
        GenericLogin(clientName, clientPem);
        // projName
        communicator.WriteStr(projName);
        if (!NetUtil.IsAck(communicator.ReadStr())) throw new ServerAuthException("Project unrecognized");
        // platform
        communicator.WriteStr(overwritePlatform ?? NetUtil.GetPlatformIdentifier());
        if (!NetUtil.IsAck(communicator.ReadStr())) throw new ServerAuthException("Platform unrecognized");
        active = true;
    }

    public void InitDev(string clientName, string clientPem, string projName) {
        Init(clientName, clientPem, projName, "dev");
        devMode = true;
    }

    private void GenericLogin(string clientName, string clientPem) {
        lock (threadSafety) { // there are other important parts to thread safe but this is most important and I'm too lazy to find all the parts.
            AssertActive();
            AssertConnectionOpen();
            this.communicator = new TcpRsaCommunicator(client, clientPem, ServerPem);
            // login
            communicator.WriteStr(clientName);
            communicator.WriteStr(communicator.ReadStr());
            if (!NetUtil.IsAck(communicator.ReadStr())) {
                Dispose();
                throw new ServerAuthException("Authentication failed");
            }

            // ver
            communicator.WriteStr(UpdaterVer.ToString());
            if (!NetUtil.IsAck(communicator.ReadStr())) {
                Dispose();
                throw new ServerAuthException("Authentication failed");
            }
        }
    }

    private void AssertConnectionOpen() {
        if (communicator.IsClosed() || !client.Connected) throw new InvalidOperationException("Connection closed when it shouldn't be!");
    }

    private void AssertActive() {
        if (!active) throw new InvalidOperationException("Connection isn't active");
    }

    public bool IsActive() {
        return active;
    }

    public bool IsDevMode() {
        if (!active) throw new InvalidOperationException("Not active");
        return devMode;
    }

    /// <summary>
    /// If you close this TcpCommunicator, bad things will happen. Please don't :)
    /// </summary>
    /// <returns></returns>
    public TcpRsaCommunicator GetCommunicator() {
        AssertActive();
        AssertConnectionOpen();
        return communicator;
    }

    private void Dispose() {
        try {
            communicator.Close();
            client.Close();
        }
        catch {
            // no-op
        } finally {
            active = false;
        }
    }

    public ServerConnection() : this(FindServer()) {
        
    }

    public ServerConnection(TcpClient client) {
        this.client = client;
        this.active = false;
    }
}