import net from "node:net";
import { discoverReady } from "./discovery.js";
export class UnityToolError extends Error {
    code;
    payload;
    constructor(code, message, payload) {
        super(message);
        this.name = "UnityToolError";
        this.code = code;
        this.payload = payload;
    }
}
const MAX_FRAME = 64 * 1024 * 1024;
/**
 * Length-prefixed JSON client for the editor bridge.
 *
 * Connection lifecycle is lazy: any request reconnects when the socket is gone or the
 * editor reloaded its domain. Requests are never auto-retried at this layer, because
 * replaying a write is not safe; the tool layer decides which reads may retry.
 */
export class UnityClient {
    socket = null;
    buffer = Buffer.alloc(0);
    pending = new Map();
    nextId = 1;
    discovery = null;
    connecting = null;
    get heartbeat() {
        return this.discovery?.heartbeat ?? null;
    }
    get projectRoot() {
        return this.discovery?.projectRoot ?? null;
    }
    async connect(force = false) {
        if (!force && this.socket && !this.socket.destroyed && this.socket.writable)
            return;
        if (this.connecting)
            return this.connecting;
        this.connecting = this.doConnect().finally(() => {
            this.connecting = null;
        });
        return this.connecting;
    }
    async doConnect() {
        this.disposeSocket();
        const discovery = await discoverReady();
        this.discovery = discovery;
        await new Promise((resolve, reject) => {
            const socket = net.connect({ host: "127.0.0.1", port: discovery.heartbeat.port });
            socket.setNoDelay(true);
            const onError = (error) => {
                socket.destroy();
                reject(new UnityToolError("bridge_connect_failed", `Could not connect to the Unity bridge on 127.0.0.1:${discovery.heartbeat.port}: ${error.message}. ` +
                    "The port may have changed after a domain reload; retry.", { port: discovery.heartbeat.port }));
            };
            socket.once("error", onError);
            socket.once("connect", () => {
                socket.off("error", onError);
                this.socket = socket;
                this.buffer = Buffer.alloc(0);
                socket.on("data", (chunk) => this.onData(chunk));
                socket.on("error", (error) => this.onSocketFailure(error));
                socket.on("close", () => this.onSocketFailure(new Error("connection closed")));
                resolve();
            });
        });
    }
    onData(chunk) {
        this.buffer = Buffer.concat([this.buffer, chunk]);
        while (this.buffer.length >= 4) {
            const length = this.buffer.readInt32LE(0);
            if (length <= 0 || length > MAX_FRAME) {
                this.onSocketFailure(new Error(`invalid frame length ${length}`));
                return;
            }
            if (this.buffer.length < 4 + length)
                return;
            const text = this.buffer.subarray(4, 4 + length).toString("utf8");
            this.buffer = this.buffer.subarray(4 + length);
            let message;
            try {
                message = JSON.parse(text);
            }
            catch {
                continue;
            }
            if (message.type === "welcome")
                continue;
            const id = message.id;
            if (id == null)
                continue;
            const waiter = this.pending.get(String(id));
            if (!waiter)
                continue;
            this.pending.delete(String(id));
            clearTimeout(waiter.timer);
            waiter.resolve(message);
        }
    }
    onSocketFailure(error) {
        const socket = this.socket;
        this.socket = null;
        this.buffer = Buffer.alloc(0);
        if (socket && !socket.destroyed)
            socket.destroy();
        for (const [id, waiter] of this.pending) {
            clearTimeout(waiter.timer);
            waiter.reject(new UnityToolError("connection_lost", "The Unity editor connection dropped (usually a domain reload). " +
                "The next call reconnects automatically; retry read-only work, and re-check state before replaying writes.", { id, cause: error.message }));
            this.pending.delete(id);
        }
    }
    disposeSocket() {
        if (this.socket) {
            try {
                this.socket.removeAllListeners();
                this.socket.destroy();
            }
            catch {
                /* ignore */
            }
            this.socket = null;
        }
    }
    async request(type, params = {}, timeoutMs = 30_000) {
        await this.connect();
        const socket = this.socket;
        if (!socket)
            throw new UnityToolError("bridge_not_connected", "Not connected to the Unity bridge.");
        const id = String(this.nextId++);
        const payload = Buffer.from(JSON.stringify({ id, type, params }), "utf8");
        const header = Buffer.alloc(4);
        header.writeInt32LE(payload.length, 0);
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                this.pending.delete(id);
                reject(new UnityToolError("timeout", `'${type}' did not answer within ${Math.round(timeoutMs / 1000)}s. ` +
                    "Long editor operations (compile, play mode, bakes) may still be running; check manage_editor.get_operation.", { type, timeoutMs }));
            }, timeoutMs);
            this.pending.set(id, { resolve, reject, timer });
            socket.write(Buffer.concat([header, payload]), (error) => {
                if (!error)
                    return;
                const waiter = this.pending.get(id);
                if (!waiter)
                    return;
                this.pending.delete(id);
                clearTimeout(waiter.timer);
                waiter.reject(new UnityToolError("write_failed", `Failed to send '${type}': ${error.message}`));
            });
        });
    }
    /** Sends a command and turns a Unity-side failure into a UnityToolError. */
    async call(type, params = {}, timeoutMs = 30_000) {
        const response = await this.request(type, params, timeoutMs);
        if (!response.success) {
            throw new UnityToolError(response.code ?? "unity_error", response.error ?? response.message ?? "The Unity bridge reported a failure.", response.data);
        }
        return response;
    }
    /** Read-only variant: reconnects once and retries, safe for idempotent queries. */
    async callRead(type, params = {}, timeoutMs = 30_000) {
        try {
            return await this.call(type, params, timeoutMs);
        }
        catch (error) {
            if (!(error instanceof UnityToolError))
                throw error;
            if (error.code !== "connection_lost" && error.code !== "bridge_connect_failed" && error.code !== "bridge_not_ready") {
                throw error;
            }
            await this.connect(true);
            return this.call(type, params, timeoutMs);
        }
    }
    close() {
        this.disposeSocket();
    }
}
