import { writeFileSync } from "node:fs";

const endpoint = process.argv[2] ?? "ws://localhost:8080/api/benchmark";
const counts = (process.argv[3] ?? "10,20,30,40,50")
  .split(",")
  .map(value => Number.parseInt(value, 10))
  .filter(value => Number.isInteger(value) && value >= 1 && value <= 50);
const outputPath = process.argv[4] ?? "docs/validation/benchmark-capacity-latest.json";

if (counts.length === 0) {
  throw new Error("Provide stream counts between 1 and 50, e.g. 10,20,30,40,50.");
}

const results = [];

for (const count of counts) {
  const separator = endpoint.includes("?") ? "&" : "?";
  const socket = new WebSocket(`${endpoint}${separator}streams=${count}`);
  const latest = new Map();
  const snapshots = {};
  let startedAt;
  let resolveRun;
  let rejectRun;
  const completed = new Promise((resolve, reject) => {
    resolveRun = resolve;
    rejectRun = reject;
  });
  const timeout = setTimeout(() => {
    socket.close();
    rejectRun(new Error(`Benchmark timed out at ${count} streams.`));
  }, 80_000);

  const capture = name => {
    snapshots[name] = {
      at: Date.now(),
      streams: [...latest.values()].map(metric => ({ ...metric })),
    };
  };

  socket.addEventListener("message", ({ data }) => {
    if (typeof data !== "string") return;

    let message;
    try {
      message = JSON.parse(data);
    } catch {
      return;
    }

    if (message.type === "ready") {
      if (message.streams !== count) {
        socket.close();
        rejectRun(new Error(`Requested ${count} streams but server configured ${message.streams}.`));
        return;
      }
      socket.send(JSON.stringify({ action: "start" }));
    } else if (message.type === "run-start") {
      startedAt = message.startedAt;
      setTimeout(() => capture("t10"), 10_000);
      setTimeout(() => capture("t30"), 30_000);
      setTimeout(() => {
        capture("t35");
        socket.send(JSON.stringify({ action: "stop" }));
      }, 35_000);
    } else if (message.type === "stream") {
      latest.set(message.id, { ...(latest.get(message.id) ?? {}), ...message });
    } else if (message.type === "fatal") {
      clearTimeout(timeout);
      socket.close();
      rejectRun(new Error(message.error));
    } else if (message.type === "run-complete") {
      clearTimeout(timeout);
      capture("end");
      socket.close();

      const start = snapshots.t10;
      const end = snapshots.t30;
      const durationSeconds = (end.at - start.at) / 1000;
      const startById = new Map(start.streams.map(metric => [metric.id, metric]));
      const sumDelta = property => end.streams.reduce((sum, metric) => {
        const before = startById.get(metric.id)?.[property] ?? 0;
        return sum + (metric[property] ?? 0) - before;
      }, 0);
      const average = property => {
        const values = end.streams.map(metric => metric[property]).filter(Number.isFinite);
        return values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
      };

      const result = {
        streams: count,
        startedAt,
        measuredSeconds: durationSeconds,
        processedDuringWindow: sumDelta("processed"),
        totalAnalyticsFps: sumDelta("processed") / durationSeconds,
        perStreamAnalyticsFps: sumDelta("processed") / durationSeconds / count,
        lateDuringWindow: sumDelta("overBudget"),
        droppedDuringWindow: sumDelta("dropped"),
        avgInferenceMs: average("inferenceAvgMs"),
        avgQueueWaitMs: average("queueWaitAvgMs"),
        avgQueueWaitP95Ms: average("queueWaitP95Ms"),
        avgLatencyP95Ms: average("latencyP95Ms"),
        avgCpuPercent: average("cpuPercent"),
        avgMemoryMb: average("memoryMb"),
        samples: snapshots,
      };

      results.push(result);
      console.log(JSON.stringify({
        streams: result.streams,
        totalAnalyticsFps: result.totalAnalyticsFps,
        perStreamAnalyticsFps: result.perStreamAnalyticsFps,
        late: result.lateDuringWindow,
        dropped: result.droppedDuringWindow,
        avgQueueWaitP95Ms: result.avgQueueWaitP95Ms,
        avgLatencyP95Ms: result.avgLatencyP95Ms,
      }));
      resolveRun(result);
    }
  });

  socket.addEventListener("error", () => {
    clearTimeout(timeout);
    rejectRun(new Error(`WebSocket connection failed for ${count} streams.`));
  });

  await completed;
  if (count !== counts.at(-1)) await new Promise(resolve => setTimeout(resolve, 2_500));
}

const report = {
  endpoint,
  generatedAt: new Date().toISOString(),
  analyticsTargetFpsPerStream: 1,
  deliveryTargetFpsPerStream: 5,
  measurementWindowSeconds: 20,
  note: "The socket uses the server's default view range: video is sent for streams 1-10 and discarded by this client. Client canvas rendering is not measured.",
  results,
};

writeFileSync(outputPath, JSON.stringify(report, null, 2));
console.log(`Saved ${outputPath}`);
