import { writeFileSync } from "node:fs";

const [endpoint = "ws://localhost:8092/api/benchmark", countArg = "30", output = "docs/validation/benchmark-full.json"] = process.argv.slice(2);
const count = Number(countArg);
if (!Number.isInteger(count) || count < 1 || count > 50) throw new Error("Stream count must be between 1 and 50");
const socket = new WebSocket(`${endpoint}?streams=${count}`);
const latest = new Map();
const snapshots = [];
let startedAt;
let ended;
let timer;
let completed = false;
const timeout = setTimeout(() => { socket.close(); throw new Error("Benchmark timeout"); }, 360_000);
socket.addEventListener("message", ({ data }) => {
  if (typeof data !== "string") return;
  const message = JSON.parse(data);
  if (message.type === "ready") {
    if (message.streams !== count) throw new Error("Unexpected stream count");
    socket.send(JSON.stringify({ action: "start" }));
  } else if (message.type === "run-start") {
    startedAt = message.startedAt;
    timer = setInterval(() => snapshots.push({ at: new Date().toISOString(), streams: [...latest.values()] }), 10_000);
  } else if (message.type === "stream") {
    latest.set(message.id, { ...(latest.get(message.id) ?? {}), ...message });
  } else if (message.type === "run-end") {
    ended = message;
  } else if (message.type === "fatal") {
    throw new Error(message.error);
  } else if (message.type === "run-complete") {
    clearTimeout(timeout);
    clearInterval(timer);
    snapshots.push({ at: new Date().toISOString(), streams: [...latest.values()] });
    const streams = [...latest.values()];
    const summary = { requestedStreams: count, reportedStreams: streams.length, startedAt, ended, processed: streams.reduce((n, x) => n + (x.processed ?? 0), 0), late: streams.reduce((n, x) => n + (x.overBudget ?? 0), 0), dropped: streams.reduce((n, x) => n + (x.dropped ?? 0), 0), errors: streams.filter(x => x.error).map(x => ({ id: x.id, error: x.error })) };
    writeFileSync(output, JSON.stringify({ summary, snapshots }, null, 2));
    console.log(JSON.stringify(summary));
    completed = true;
    socket.close();
  }
});
socket.addEventListener("error", () => { if (!completed) { clearTimeout(timeout); clearInterval(timer); throw new Error("WebSocket connection failed"); } });
