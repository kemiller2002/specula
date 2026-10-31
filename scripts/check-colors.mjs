// VISUAL-TOKENS: validate application CSS against the pinned Forma release's
// color contract, and refuse raw colour literals in application CSS.
import { readFileSync } from "node:fs";
import { validateColorFiles } from "@echelon-foundry/design-system/color-contract";

const files = ["web/app.css"];
const contractFindings = validateColorFiles(files);
const literal = /#[0-9a-fA-F]{3,8}\b|\b(?:rgb|rgba|hsl|hsla|oklch|lab|lch)\(|(?<![\w-])(?:white|black|red|green|blue|gray|grey)(?![\w-])/;
const literalFindings = files.flatMap((file) =>
  readFileSync(file, "utf8").split("\n").flatMap((line, i) =>
    literal.test(line.replace(/\/\*.*?\*\//g, "")) ? [{ file, line: i + 1, message: `raw colour literal: ${line.trim()}` }] : []));
const findings = [...contractFindings, ...literalFindings];
for (const f of findings) console.error(JSON.stringify(f));
if (findings.length > 0) process.exit(1);
console.log(`Color contract: ${files.length} file(s) valid against Forma ${JSON.parse(readFileSync("node_modules/@echelon-foundry/design-system/package.json", "utf8")).version}; no raw colour literals.`);
