import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
const HEARTBEAT_NAME = ".codely-unity-mcp.json";
function ancestors(start) {
    const result = [];
    let dir = path.resolve(start);
    for (let i = 0; i < 10; i++) {
        result.push(dir);
        const parent = path.dirname(dir);
        if (parent === dir)
            break;
        dir = parent;
    }
    return result;
}
/** Project root implied by this server's own location: <root>/Packages/<pkg>/Server~/dist */
function packageRoot() {
    try {
        const distDir = path.dirname(fileURLToPath(import.meta.url));
        return path.resolve(distDir, "..", "..", "..", "..");
    }
    catch {
        return null;
    }
}
export function candidateRoots() {
    const candidates = [];
    const env = process.env.UNITY_MCP_PROJECT;
    if (env)
        candidates.push(path.resolve(env));
    const pkg = packageRoot();
    if (pkg)
        candidates.push(pkg);
    candidates.push(...ancestors(process.cwd()));
    return [...new Set(candidates.map((p) => path.resolve(p)))];
}
function readJson(file) {
    try {
        const raw = fs.readFileSync(file, "utf8");
        if (!raw.trim())
            return null;
        return JSON.parse(raw);
    }
    catch {
        return null;
    }
}
/**
 * Locates the running editor through the heartbeat file the bridge writes into
 * <project>/Temp. Never guesses a port: a wrong port is worse than a clear error.
 */
export function discover() {
    const attempts = [];
    for (const root of candidateRoots()) {
        const heartbeatPath = path.join(root, "Temp", HEARTBEAT_NAME);
        attempts.push(heartbeatPath);
        if (!fs.existsSync(heartbeatPath))
            continue;
        const heartbeat = readJson(heartbeatPath);
        if (!heartbeat)
            continue;
        return { projectRoot: root, heartbeatPath, heartbeat };
    }
    const hint = "Could not find the Unity MCP bridge heartbeat. Open the Unity/Tuanjie editor with the " +
        "'com.codely.unity-mcp' package installed, or set UNITY_MCP_PROJECT to the project root " +
        "(the folder that contains Assets/).";
    const error = new Error(`${hint}\nChecked:\n${attempts.map((a) => "  - " + a).join("\n")}`);
    error.code = "bridge_not_found";
    throw error;
}
/** Waits until the editor is out of a domain reload and reports a usable port. */
export async function discoverReady(timeoutMs = 90_000, pollMs = 250, isAborted) {
    const deadline = Date.now() + timeoutMs;
    let lastReason = "unknown";
    while (Date.now() < deadline) {
        if (isAborted?.())
            throw new Error("aborted");
        try {
            const discovery = discover();
            const state = discovery.heartbeat.state;
            const port = discovery.heartbeat.port;
            lastReason = `state=${state} port=${port}`;
            if (state === "ready" && port > 0)
                return discovery;
        }
        catch (error) {
            lastReason = error.message.split("\n")[0];
        }
        await new Promise((resolve) => setTimeout(resolve, pollMs));
    }
    const error = new Error(`The Unity editor did not become ready within ${Math.round(timeoutMs / 1000)}s (${lastReason}). ` +
        "If a domain reload is in progress it usually finishes within a few seconds; retry then.");
    error.code = "bridge_not_ready";
    throw error;
}
