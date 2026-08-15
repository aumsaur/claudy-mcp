import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { spawn } from "node:child_process";
import net from "node:net";
import { mkdtemp, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PROMPT_SCRIPT = path.join(__dirname, "prompt.ps1");
const PET_EXE = path.join(__dirname, "PetOverlay", "bin", "Release", "net10.0-windows", "PetOverlay.exe");
const PET_PIPE_PATH = "\\\\.\\pipe\\ClaudeAskUserPet";
const DEFAULT_TIMEOUT_SECONDS = 300;

function connectPetPipe(timeoutMs) {
    return new Promise((resolve, reject) => {
        const socket = net.createConnection({ path: PET_PIPE_PATH });
        const timer = setTimeout(() => {
            socket.destroy();
            reject(new Error("pipe connect timeout"));
        }, timeoutMs);
        socket.once("connect", () => {
            clearTimeout(timer);
            resolve(socket);
        });
        socket.once("error", (err) => {
            clearTimeout(timer);
            reject(err);
        });
    });
}

// Opens the ONE connection actually used for the request/response, launching the
// pet and retrying if it isn't up yet. Deliberately avoids a separate "probe"
// connect-then-reconnect: the pipe server only allows one instance at a time
// (maxNumberOfServerInstances: 1), so a probe connection racing its own teardown
// against a second real connection can transiently fail with a busy pipe and
// silently punt to the PowerShell fallback.
async function connectToPetOrLaunch() {
    try {
        return await connectPetPipe(1500);
    } catch {
        // not running yet - fall through and try to launch it
    }

    try {
        const child = spawn(PET_EXE, [], { detached: true, stdio: "ignore", windowsHide: true });
        child.unref();
    } catch (err) {
        throw new Error(`could not launch pet: ${err.message}`);
    }

    for (let i = 0; i < 15; i++) {
        await new Promise((r) => setTimeout(r, 300));
        try {
            return await connectPetPipe(800);
        } catch {
            // keep waiting for it to come up
        }
    }
    throw new Error("pet did not come up in time");
}

function runPromptViaPet(request, timeoutSeconds) {
    return new Promise(async (resolve, reject) => {
        let socket;
        try {
            socket = await connectToPetOrLaunch();
        } catch (err) {
            return reject(err);
        }

        let buf = "";
        let settled = false;
        const safetyTimer = setTimeout(() => {
            if (settled) return;
            settled = true;
            socket.destroy();
            reject(new Error("pet did not respond in time"));
        }, (timeoutSeconds + 20) * 1000);

        socket.on("data", (chunk) => {
            if (settled) return;
            buf += chunk.toString("utf8");
            const nl = buf.indexOf("\n");
            if (nl === -1) return;
            settled = true;
            clearTimeout(safetyTimer);
            socket.end();
            try {
                resolve(JSON.parse(buf.slice(0, nl)));
            } catch (err) {
                reject(err);
            }
        });

        socket.on("error", (err) => {
            if (settled) return;
            settled = true;
            clearTimeout(safetyTimer);
            reject(err);
        });

        socket.write(JSON.stringify(request) + "\n");
    });
}

function runPromptViaPowerShell(request, timeoutSeconds) {
    return new Promise(async (resolve, reject) => {
        const dir = await mkdtemp(path.join(tmpdir(), "ask-user-"));
        const requestPath = path.join(dir, "request.json");
        const responsePath = path.join(dir, "response.json");

        const cleanup = () => rm(dir, { recursive: true, force: true }).catch(() => {});

        try {
            await writeFile(requestPath, JSON.stringify(request), "utf8");
        } catch (err) {
            await cleanup();
            return reject(err);
        }

        const child = spawn(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-WindowStyle", "Hidden",
                "-File", PROMPT_SCRIPT,
                "-RequestPath", requestPath,
                "-ResponsePath", responsePath,
            ],
            { windowsHide: true }
        );

        let settled = false;
        const killTimer = setTimeout(() => {
            if (!settled) child.kill();
        }, (timeoutSeconds + 15) * 1000);

        child.on("error", async (err) => {
            if (settled) return;
            settled = true;
            clearTimeout(killTimer);
            await cleanup();
            reject(err);
        });

        child.on("exit", async () => {
            if (settled) return;
            settled = true;
            clearTimeout(killTimer);
            let result = { status: "cancelled", answer: null };
            try {
                const raw = await readFile(responsePath, "utf8");
                const bom = String.fromCharCode(0xfeff);
                result = JSON.parse(raw.startsWith(bom) ? raw.slice(bom.length) : raw);
            } catch {
                // no response file written (window closed abnormally, etc.)
            }
            await cleanup();
            resolve(result);
        });
    });
}

const server = new McpServer({ name: "ask-user", version: "0.1.0" });

server.registerTool(
    "ask_user",
    {
        title: "Ask User",
        description:
            "Show the user a small on-screen prompt (like a toast notification, bottom-right of their " +
            "primary screen) and wait for their response. Use this when you need a decision, a choice " +
            "among options, or free-text input from the user, especially when they may not be reading " +
            "the chat right now. Blocks until the user answers or the prompt times out.",
        inputSchema: {
            question: z.string().min(1).describe("The question or request to show the user."),
            kind: z
                .enum(["yesno", "choice", "text"])
                .default("yesno")
                .describe(
                    "'yesno' shows Yes/No buttons. 'choice' shows a button per entry in `options` " +
                        "(pick one). 'text' shows a text box for free-form input."
                ),
            options: z
                .array(z.string().min(1))
                .min(2)
                .optional()
                .describe("Required when kind is 'choice': the list of options to present, in order."),
            placeholder: z
                .string()
                .optional()
                .describe("Optional hint text shown above the input box when kind is 'text'."),
            timeoutSeconds: z
                .number()
                .int()
                .positive()
                .max(3600)
                .optional()
                .describe("Seconds to wait before the prompt auto-dismisses. Default 300 (5 minutes)."),
        },
    },
    async ({ question, kind, options, placeholder, timeoutSeconds }) => {
        const effectiveKind = kind ?? "yesno";
        const effectiveTimeout = timeoutSeconds ?? DEFAULT_TIMEOUT_SECONDS;

        if (effectiveKind === "choice" && (!options || options.length < 2)) {
            return {
                isError: true,
                content: [
                    { type: "text", text: "kind is 'choice' but `options` was missing or had fewer than 2 entries." },
                ],
            };
        }

        const payload = { question, kind: effectiveKind, options, placeholder, timeoutSeconds: effectiveTimeout };

        let result;
        try {
            result = await runPromptViaPet(payload, effectiveTimeout);
        } catch (petErr) {
            console.error(`[ask-user] pet unreachable, falling back to plain popup: ${petErr.message}`);
            try {
                result = await runPromptViaPowerShell(payload, effectiveTimeout);
            } catch (err) {
                return {
                    isError: true,
                    content: [{ type: "text", text: `Failed to show prompt: ${err.message}` }],
                };
            }
        }

        if (result.status === "answered") {
            return { content: [{ type: "text", text: `User answered: ${result.answer}` }] };
        }
        if (result.status === "timeout") {
            return {
                content: [
                    { type: "text", text: `No response — the prompt timed out after ${effectiveTimeout}s.` },
                ],
            };
        }
        return { content: [{ type: "text", text: "User dismissed the prompt without answering." }] };
    }
);

const transport = new StdioServerTransport();
await server.connect(transport);
