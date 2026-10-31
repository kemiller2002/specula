// SPEC-V-010: the dependency manifest, lockfile, project files, vendored
// packages and the versions shown in the About screen must all agree.
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";

const read = (p) => readFileSync(p, "utf8");
const baseline = JSON.parse(read("dependency-baseline.json"));
const lock = JSON.parse(read("package-lock.json"));
const pkg = JSON.parse(read("package.json"));
const fsproj = read("src/Specula.Engine/Specula.Engine.fsproj");
const view = read("src/Specula.Engine/View.fs");
const failures = [];
const expect = (ok, message) => { if (!ok) failures.push(message); };

for (const c of baseline.components) {
  if (c.registry === "npm") {
    const declared = pkg.dependencies?.[c.name] ?? pkg.devDependencies?.[c.name];
    const locked = lock.packages?.[`node_modules/${c.name}`]?.version;
    expect(declared === c.version, `${c.name}: package.json pins ${declared}, baseline says ${c.version}`);
    expect(locked === c.version, `${c.name}: package-lock has ${locked}, baseline says ${c.version}`);
  }
  if (c.name.startsWith("EchelonFoundry.")) {
    expect(fsproj.includes(`Include="${c.name}" Version="${c.version}"`), `${c.name}: fsproj does not pin ${c.version}`);
    const file = `vendor/nuget/${c.name}.${c.version}.nupkg`;
    const digest = createHash("sha256").update(readFileSync(file)).digest("hex");
    expect(digest === c.sha256, `${file}: sha256 ${digest} does not match baseline ${c.sha256}`);
  }
}
for (const [name] of [["@echelon-foundry/limen", "0.9.1"], ["@echelon-foundry/design-system", "0.13.0"]]) {
  const c = baseline.components.find((x) => x.name === name);
  expect(view.includes(`"${c.version}"`), `About screen does not show ${name} ${c.version}`);
}
for (const f of failures) console.error(f);
if (failures.length) process.exit(1);
console.log(`Dependency baseline consistent: ${baseline.components.length} components.`);
