import dgram, { type RemoteInfo } from "node:dgram";
import { RollingBandwidthMeter } from "./rolling-bandwidth.js";

const MAGIC = Buffer.from("STS2R1", "ascii");
const MESSAGE_TYPE_HOST_REGISTER = 1;
const MESSAGE_TYPE_HOST_DATA = 2;
const MESSAGE_TYPE_CLIENT_DATA = 3;

export interface RelayEndpoint {
  host: string;
  port: number;
}

export interface RelayManagerConfig {
  bindHost: string;
  portStart: number;
  portEnd: number;
  hostIdleMs: number;
  clientIdleMs: number;
}

export interface RelayLogEvent {
  phase: string;
  roomId: string;
  detail: string;
}

export interface RelayTrafficSnapshot {
  currentBandwidthMbps: number;
  totalBytesInWindow: number;
  windowMs: number;
  activeRooms: number;
  activeHosts: number;
  activeClients: number;
}

interface ClientBinding {
  endpoint: RemoteInfo;
  clientId: number;
  lastSeenAt: number;
}

interface ParsedHostMessage {
  type: "host_register" | "host_data";
  token?: string;
  clientId?: number;
  payload?: Buffer;
}

class RoomRelaySession {
  private readonly clientsByKey = new Map<string, ClientBinding>();
  private readonly clientsById = new Map<number, ClientBinding>();
  private readonly socket: dgram.Socket;
  private advertisedHost = "";
  private hostEndpoint: RemoteInfo | undefined;
  private hostLastSeenAt = 0;
  private nextClientId = 1;

  constructor(
    readonly roomId: string,
    readonly hostToken: string,
    readonly port: number,
    bindHost: string,
    private readonly config: RelayManagerConfig,
    private readonly logEvent: (event: RelayLogEvent) => void,
    private readonly recordTraffic: (bytes: number) => void,
  ) {
    this.socket = dgram.createSocket("udp4");
    this.socket.on("message", (message, remote) => {
      this.handleMessage(message, remote);
    });
    this.socket.on("error", (error) => {
      this.logEvent({
        phase: "relay_socket_error",
        roomId: this.roomId,
        detail: `${error.name}: ${error.message}`,
      });
    });
    this.socket.bind(this.port, bindHost, () => {
      this.logEvent({
        phase: "relay_listening",
        roomId: this.roomId,
        detail: `udp://${bindHost}:${this.port}`,
      });
    });
  }

  setAdvertisedHost(host: string) {
    this.advertisedHost = host.trim();
  }

  getEndpoint() {
    if (!this.advertisedHost) {
      return null;
    }

    return {
      host: this.advertisedHost,
      port: this.port,
    };
  }

  hasActiveHost() {
    return this.hostEndpoint != null;
  }

  getActiveHostDetail() {
    if (!this.hostEndpoint) {
      return null;
    }

    return `${this.hostEndpoint.address}:${this.hostEndpoint.port}`;
  }

  getClientCount() {
    return this.clientsById.size;
  }

  cleanup(now = Date.now()) {
    if (this.hostEndpoint && now - this.hostLastSeenAt > this.config.hostIdleMs) {
      this.logEvent({
        phase: "relay_host_idle",
        roomId: this.roomId,
        detail: `${this.hostEndpoint.address}:${this.hostEndpoint.port}`,
      });
      this.hostEndpoint = undefined;
      this.hostLastSeenAt = 0;
    }

    for (const [key, client] of this.clientsByKey.entries()) {
      if (now - client.lastSeenAt <= this.config.clientIdleMs) {
        continue;
      }

      this.clientsByKey.delete(key);
      this.clientsById.delete(client.clientId);
      this.logEvent({
        phase: "relay_client_idle",
        roomId: this.roomId,
        detail: `clientId=${client.clientId} endpoint=${key}`,
      });
    }
  }

  close() {
    this.socket.close();
    this.clientsByKey.clear();
    this.clientsById.clear();
    this.hostEndpoint = undefined;
    this.hostLastSeenAt = 0;
  }

  private handleMessage(message: Buffer, remote: RemoteInfo) {
    this.recordTraffic(message.byteLength);
    const parsed = parseHostMessage(message);
    if (parsed) {
      this.handleHostMessage(parsed, remote);
      return;
    }

    this.handleClientPayload(message, remote);
  }

  private handleHostMessage(message: ParsedHostMessage, remote: RemoteInfo) {
    if (message.type === "host_register") {
      if (!message.token || message.token !== this.hostToken) {
        this.logEvent({
          phase: "relay_host_register_rejected",
          roomId: this.roomId,
          detail: `${remote.address}:${remote.port}`,
        });
        return;
      }

      const endpointChanged = !sameEndpoint(this.hostEndpoint, remote);
      this.hostEndpoint = remote;
      this.hostLastSeenAt = Date.now();
      if (endpointChanged) {
        this.logEvent({
          phase: "relay_host_registered",
          roomId: this.roomId,
          detail: `${remote.address}:${remote.port}`,
        });
      }
      return;
    }

    if (!this.hostEndpoint || !sameEndpoint(this.hostEndpoint, remote)) {
      this.logEvent({
        phase: "relay_host_data_rejected",
        roomId: this.roomId,
        detail: `${remote.address}:${remote.port}`,
      });
      return;
    }

    this.hostLastSeenAt = Date.now();
    if (!message.payload || message.clientId == null) {
      return;
    }

    const client = this.clientsById.get(message.clientId);
    if (!client) {
      return;
    }

    client.lastSeenAt = Date.now();
    this.recordTraffic(message.payload.byteLength);
    this.socket.send(message.payload, client.endpoint.port, client.endpoint.address);
  }

  private handleClientPayload(message: Buffer, remote: RemoteInfo) {
    if (!this.hostEndpoint) {
      return;
    }

    const clientKey = `${remote.address}:${remote.port}`;
    let client = this.clientsByKey.get(clientKey);
    if (!client) {
      client = {
        endpoint: remote,
        clientId: this.nextClientId++,
        lastSeenAt: Date.now(),
      };
      this.clientsByKey.set(clientKey, client);
      this.clientsById.set(client.clientId, client);
      this.logEvent({
        phase: "relay_client_connected",
        roomId: this.roomId,
        detail: `clientId=${client.clientId} endpoint=${clientKey}`,
      });
    } else {
      client.lastSeenAt = Date.now();
    }

    const envelope = encodeClientData(client.clientId, message);
    this.recordTraffic(envelope.byteLength);
    this.socket.send(envelope, this.hostEndpoint.port, this.hostEndpoint.address);
  }
}

export class RoomRelayManager {
  private readonly sessions = new Map<string, RoomRelaySession>();
  private readonly portsInUse = new Set<number>();
  private readonly cleanupTimer: NodeJS.Timeout;
  private readonly bandwidthMeter = new RollingBandwidthMeter(30_000);

  constructor(
    private readonly config: RelayManagerConfig,
    private readonly logEvent: (event: RelayLogEvent) => void,
  ) {
    this.cleanupTimer = setInterval(() => {
      for (const session of this.sessions.values()) {
        session.cleanup();
      }
    }, 5000);
    this.cleanupTimer.unref();
  }

  allocateRoom(roomId: string, hostToken: string, advertisedHost: string) {
    const existing = this.sessions.get(roomId);
    if (existing) {
      existing.setAdvertisedHost(advertisedHost);
      return existing.getEndpoint();
    }

    const port = this.reservePort();
    if (port == null) {
      this.logEvent({
        phase: "relay_allocation_failed",
        roomId,
        detail: `no free UDP port in range ${this.config.portStart}-${this.config.portEnd}`,
      });
      return null;
    }

    const session = new RoomRelaySession(
      roomId,
      hostToken,
      port,
      this.config.bindHost,
      this.config,
      this.logEvent,
      (bytes) => {
        this.bandwidthMeter.recordBytes(bytes);
      },
    );
    session.setAdvertisedHost(advertisedHost);
    this.sessions.set(roomId, session);
    this.logEvent({
      phase: "relay_allocated",
      roomId,
      detail: `udp://${advertisedHost}:${port}`,
    });
    return session.getEndpoint();
  }

  getRoomEndpoint(roomId: string, advertisedHost: string) {
    const session = this.sessions.get(roomId);
    if (!session) {
      return null;
    }

    session.setAdvertisedHost(advertisedHost);
    return session.getEndpoint();
  }

  getRoomStatus(roomId: string) {
    const session = this.sessions.get(roomId);
    if (!session) {
      return {
        hasSession: false,
        hasActiveHost: false,
        activeHostDetail: null,
        clientCount: 0,
      };
    }

    return {
      hasSession: true,
      hasActiveHost: session.hasActiveHost(),
      activeHostDetail: session.getActiveHostDetail(),
      clientCount: session.getClientCount(),
    };
  }

  removeRoom(roomId: string) {
    const session = this.sessions.get(roomId);
    if (!session) {
      return;
    }

    session.close();
    this.sessions.delete(roomId);
    this.portsInUse.delete(session.port);
    this.logEvent({
      phase: "relay_removed",
      roomId,
      detail: `released udp port ${session.port}`,
    });
  }

  close() {
    clearInterval(this.cleanupTimer);
    for (const session of this.sessions.values()) {
      session.close();
    }

    this.sessions.clear();
    this.portsInUse.clear();
  }

  getCurrentBandwidthMbps(now = Date.now()) {
    return this.bandwidthMeter.getSnapshot(now).currentBandwidthMbps;
  }

  getTrafficSnapshot(now = Date.now()): RelayTrafficSnapshot {
    const bandwidthSnapshot = this.bandwidthMeter.getSnapshot(now);
    let activeHosts = 0;
    let activeClients = 0;

    for (const session of this.sessions.values()) {
      if (session.hasActiveHost()) {
        activeHosts += 1;
      }

      activeClients += session.getClientCount();
    }

    return {
      currentBandwidthMbps: bandwidthSnapshot.currentBandwidthMbps,
      totalBytesInWindow: bandwidthSnapshot.totalBytesInWindow,
      windowMs: bandwidthSnapshot.windowMs,
      activeRooms: this.sessions.size,
      activeHosts,
      activeClients,
    };
  }

  private reservePort() {
    for (let port = this.config.portStart; port <= this.config.portEnd; port += 1) {
      if (this.portsInUse.has(port)) {
        continue;
      }

      this.portsInUse.add(port);
      return port;
    }

    return null;
  }
}

function sameEndpoint(left: RemoteInfo | undefined, right: RemoteInfo) {
  if (!left) {
    return false;
  }

  return left.address === right.address && left.port === right.port;
}

function parseHostMessage(buffer: Buffer): ParsedHostMessage | null {
  if (buffer.length < MAGIC.length + 1 || !buffer.subarray(0, MAGIC.length).equals(MAGIC)) {
    return null;
  }

  const type = buffer.readUInt8(MAGIC.length);
  if (type === MESSAGE_TYPE_HOST_REGISTER) {
    if (buffer.length < MAGIC.length + 3) {
      return null;
    }

    const tokenLength = buffer.readUInt16BE(MAGIC.length + 1);
    const tokenStart = MAGIC.length + 3;
    const tokenEnd = tokenStart + tokenLength;
    if (buffer.length < tokenEnd) {
      return null;
    }

    return {
      type: "host_register",
      token: buffer.subarray(tokenStart, tokenEnd).toString("utf8"),
    };
  }

  if (type === MESSAGE_TYPE_HOST_DATA) {
    if (buffer.length < MAGIC.length + 5) {
      return null;
    }

    return {
      type: "host_data",
      clientId: buffer.readUInt32BE(MAGIC.length + 1),
      payload: buffer.subarray(MAGIC.length + 5),
    };
  }

  return null;
}

function encodeClientData(clientId: number, payload: Buffer) {
  const header = Buffer.allocUnsafe(MAGIC.length + 5);
  MAGIC.copy(header, 0);
  header.writeUInt8(MESSAGE_TYPE_CLIENT_DATA, MAGIC.length);
  header.writeUInt32BE(clientId >>> 0, MAGIC.length + 1);
  return Buffer.concat([header, payload]);
}
