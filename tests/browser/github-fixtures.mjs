// Deterministic fake GitHub for browser tests. A scenario is plain data;
// `install` routes api.github.com and raw.githubusercontent.com to it.

export { commit };
export const HEAD = "b".repeat(40);
export const OLD = "a".repeat(40);

const at = (minutes) => new Date(Date.UTC(2026, 9, 30, 10, 0, 0) + minutes * 60_000).toISOString();

const commit = (sha, message, minutes) => ({
  sha,
  html_url: `https://github.com/octo/demo/commit/${sha}`,
  commit: { message, author: { name: "Alice", date: at(minutes) }, committer: { date: at(minutes) } },
  author: { login: "alice" },
});

const runOf = (id, name, sha, conclusion, minutes, status = "completed") => ({
  id, name, head_sha: sha, head_branch: "main", event: "push", status, conclusion, run_attempt: 1,
  created_at: at(minutes), updated_at: at(minutes + 5), html_url: `https://github.com/octo/demo/actions/runs/${id}`,
});

const checkOf = (id, name, sha, conclusion, status = "completed") => ({
  id, name, head_sha: sha, status, conclusion, app: { name: "GitHub Actions" },
  started_at: at(60), completed_at: at(65), html_url: `https://github.com/octo/demo/runs/${id}`,
});

export const baseScenario = (overrides = {}) => ({
  repository: (owner, name) => ({ name, owner: { login: owner }, default_branch: "main", private: false, html_url: `https://github.com/${owner}/${name}`, created_at: at(-600) }),
  commits: [commit(HEAD, "Implement feature", 60), commit(OLD, "Initial commit", 0)],
  pulls: [{ number: 3, title: "Add feature", state: "open", user: { login: "bob" }, created_at: at(30), updated_at: at(40), head: { sha: HEAD }, html_url: "https://github.com/octo/demo/pull/3" }],
  runs: [runOf(11, "ci", OLD, "success", 1), runOf(12, "ci", HEAD, "success", 61)],
  checks: [checkOf(21, "build", HEAD, "success")],
  deployments: [],
  deploymentStatuses: {},
  evidence: {},
  branchHead: HEAD,
  ...overrides,
});

export const scenarios = {
  passing: () => baseScenario(),
  failedHead: () => baseScenario({ runs: [runOf(11, "ci", OLD, "success", 1), runOf(12, "ci", HEAD, "failure", 61)], checks: [checkOf(21, "build", HEAD, "failure")] }),
  oldGreenNewUnverified: () => baseScenario({ runs: [runOf(11, "ci", OLD, "success", 1)], checks: [] }),
  noActions: () => baseScenario({ runs: [], checks: [], runsDisabled: true }),
  deploymentNoSmoke: () =>
    baseScenario({
      deployments: [{ id: 501, sha: HEAD, ref: "main", environment: "github-pages", creator: { login: "github-actions[bot]" }, created_at: at(70) }],
      deploymentStatuses: { 501: [{ id: 1, state: "success", created_at: at(71), environment_url: "https://demo.example.org/" }] },
    }),
  unsupportedEvidence: () => baseScenario({ evidence: { "evidence/traceability.json": JSON.stringify({ schema: "causa.traceability/9", requirements: [] }) } }),
  supportedEvidence: () =>
    baseScenario({
      evidence: {
        "evidence/traceability.json": JSON.stringify({ schema: "causa.traceability/1", scope: "P0", requirements: [
          { id: "UI-001", title: "Dashboard", priority: "P0", status: "VERIFIED", evidence: "e2e" },
          { id: "UI-002", title: "Timeline", priority: "P0", status: "BLOCKED", evidence: "" },
        ] }),
        "evidence/DECISIONS.md": "| ID | Decision | Why | Effect |\n|---|---|---|---|\n| KD-001 | Use Limen | Boundary | F# engine |\n",
        "evidence/kickoff/kickoff.json": JSON.stringify({ schema: "indy-init.rehearsal-kickoff/1", t0: at(0), deadline: at(420), timeLimitMinutes: 420 }),
      },
    }),
  longNames: () =>
    baseScenario({
      repository: (owner, name) => ({ name, owner: { login: owner }, default_branch: "feature/" + "very-long-branch-name-".repeat(5), private: false, html_url: `https://github.com/${owner}/${name}` }),
      commits: [commit(HEAD, "A".repeat(20) + " " + "extraordinarily-long-commit-subject-without-spaces-".repeat(4), 60), commit(OLD, "x", 0)],
    }),
};

const json = (body, status = 200, headers = {}) => ({
  status,
  contentType: "application/json",
  headers: { "access-control-allow-origin": "*", "access-control-expose-headers": "ETag, Link, X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset, Retry-After", "x-ratelimit-limit": "60", "x-ratelimit-remaining": "55", ...headers },
  body: JSON.stringify(body),
});

export const respond = (scenario, url) => {
  const u = new URL(url);
  if (u.hostname === "raw.githubusercontent.com") {
    const path = u.pathname.split("/").slice(4).join("/");
    const text = scenario.evidence[path];
    return text === undefined
      ? { status: 404, contentType: "text/plain", headers: { "access-control-allow-origin": "*" }, body: "404: Not Found" }
      : { status: 200, contentType: "text/plain", headers: { "access-control-allow-origin": "*" }, body: text };
  }
  const parts = u.pathname.split("/").filter(Boolean); // repos, owner, name, ...
  const [, owner, name, kind, ...rest] = parts;
  if (scenario.missing?.includes(`${owner}/${name}`)) return json({ message: "Not Found" }, 404);
  if (scenario.rateLimited) return json({ message: "API rate limit exceeded" }, 403, { "x-ratelimit-remaining": "0", "x-ratelimit-reset": String(Math.floor(Date.now() / 1000) + 1800) });
  switch (kind) {
    case undefined: return json(scenario.repository(owner, name));
    case "commits":
      if (rest[1] === "check-runs") return json({ total_count: scenario.checks.length, check_runs: scenario.checks });
      return json(scenario.commitsByRepo?.[`${owner}/${name}`] ?? scenario.commits);
    case "pulls": return json(scenario.pulls);
    case "actions": return scenario.runsDisabled ? json({ total_count: 0, workflow_runs: [] }) : json({ total_count: scenario.runs.length, workflow_runs: scenario.runs });
    case "deployments":
      if (rest[1] === "statuses") return json(scenario.deploymentStatuses[rest[0]] ?? []);
      return json(scenario.deployments);
    case "branches": return json({ commit: { sha: scenario.branchHead } });
    default: return json({ message: "Not Found" }, 404);
  }
};

/** Route GitHub hosts to the scenario. `delay(url)` may return milliseconds. */
export const install = async (page, scenario, { delay = () => 0, log = [] } = {}) => {
  const handler = async (route) => {
    const url = route.request().url();
    log.push(url);
    const ms = delay(url);
    if (ms > 0) await new Promise((r) => setTimeout(r, ms));
    const current = typeof scenario === "function" ? scenario() : scenario;
    if (current.offline) return route.abort("internetdisconnected").catch(() => {});
    await route.fulfill(respond(current, url)).catch(() => {});
  };
  await page.route("https://api.github.com/**", handler);
  await page.route("https://raw.githubusercontent.com/**", handler);
};

export const ready = (page) => page.waitForFunction(() => document.documentElement.dataset.speculaReady === "true");

export const choose = async (page, repo) => {
  await page.goto("/");
  await ready(page);
  await page.getByLabel("GitHub repository").fill(repo);
  await page.getByRole("button", { name: "Observe repository" }).click();
  await page.locator("h1", { hasText: "Overview" }).waitFor();
};

export const settled = (page) => page.locator(".app-status .ef-status-lozenge").filter({ hasNotText: /Connecting|Refreshing/ }).waitFor();
