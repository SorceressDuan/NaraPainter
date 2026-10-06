// One-shot product rename: NaraPainter -> NaraPainter.
//
// The identifiers the build and the code use all change. Two things deliberately do not: the Chinese
// display name, which is a value in the resource files and never a literal here, and the provenance
// sentences about the upstream macOS project, which this script does not match because it only
// rewrites the product identifier.
//
// The launcher project becomes NaraPainter.Bootstrap rather than keeping the product name, because
// the payload already has a NaraPainter.exe inside app\ and two assemblies under one name is a trap
// for anyone reading a stack trace.
//
// Run from the repository root:  node tools/rename/rename-to-narapainter.js --dry
const fs = require("fs");
const path = require("path");

const OLD = "NaraPainter";
const NEW = "NaraPainter";
const BOOTSTRAP = "NaraPainter.Bootstrap";
const dryRun = process.argv.includes("--dry");

const root = path.resolve(__dirname, "..", "..");

const SKIP_DIRS = new Set([".git", ".tools", "bin", "obj", "dist", "legacy", "node_modules"]);

const TEXT_EXT = new Set([
    ".cs", ".xaml", ".csproj", ".sln", ".props", ".targets", ".md", ".ps1", ".txt",
    ".json", ".manifest", ".appxmanifest", ".resx", ".config", ".xml", ".bat", ".js"
]);

// The launcher's assembly name has to be decided before the generic rules run, so that the specific
// rule wins. Everything else is the plain identifier swap.
const RULES = [
    // The launcher project and its assembly.
    ["NaraPainter.Bootstrap", BOOTSTRAP],
    ["NaraPainter.Bootstrap.csproj", BOOTSTRAP + ".csproj"],
    // Namespace-bearing identifiers, longest first.
    [`${OLD}.App`, `${NEW}.App`],
    [`${OLD}.Models`, `${NEW}.Models`],
    [`${OLD}.Imaging`, `${NEW}.Imaging`],
    [`${OLD}.Compositing`, `${NEW}.Compositing`],
    [`${OLD}.Tests`, `${NEW}.Tests`],
    [`${OLD}.TestRunner`, `${NEW}.TestRunner`],
    // Bare namespace prefix: "namespace NaraPainter;" and "using NaraPainter;"
    [`namespace ${OLD};`, `namespace ${NEW};`],
    [`using ${OLD};`, `using ${NEW};`],
    // Assembly and executable names, in every place they are spelled out.
    [`<AssemblyName>${OLD}</AssemblyName>`, `<AssemblyName>${NEW}</AssemblyName>`],
    [`${OLD}.exe`, `${NEW}.exe`],
    [`${OLD}.dll`, `${NEW}.dll`],
    [`${OLD}.deps.json`, `${NEW}.deps.json`],
    [`${OLD}.runtimeconfig.json`, `${NEW}.runtimeconfig.json`],
    [`${OLD}.sln`, `${NEW}.sln`],
    [`${OLD}.ico`, `${NEW}.ico`],
    [`${OLD}.lnk`, `${NEW}.lnk`],
    [`${OLD}.csproj`, `${NEW}.csproj`],
    // Log tags and scratch file names.
    ["narapainter-selftest", "narapainter-selftest"],
    ["narapainter-startup", "narapainter-startup"],
    ["narapainter-not-an-image", "narapainter-not-an-image"],
    // Any remaining occurrence: paths, quoted strings, prose about this program.
    [OLD, NEW]
];

function isTextFile(name) {
    return TEXT_EXT.has(path.extname(name).toLowerCase());
}

function walk(dir, out = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (entry.isDirectory()) {
            if (SKIP_DIRS.has(entry.name)) continue;
            walk(path.join(dir, entry.name), out);
        } else {
            out.push(path.join(dir, entry.name));
        }
    }
    return out;
}

function applyRules(text) {
    let result = text;
    let hits = 0;
    for (const [from, to] of RULES) {
        const parts = result.split(from);
        if (parts.length > 1) {
            hits += parts.length - 1;
            result = parts.join(to);
        }
    }
    return { result, hits };
}

function renameSegment(segment) {
    if (segment === OLD) return NEW;
    if (segment.startsWith(OLD + ".")) return NEW + segment.slice(OLD.length);
    return segment;
}

function renamePath(relPath) {
    const parts = relPath.split(path.sep);
    let changed = false;
    const mapped = parts.map((part) => {
        const next = renameSegment(part);
        if (next !== part) changed = true;
        return next;
    });
    return { path: mapped.join(path.sep), changed };
}

function movePath(from, to) {
    if (dryRun) return;
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.renameSync(from, to);
}

function main() {
    const files = walk(root);
    let rewrittenFiles = 0;
    let totalHits = 0;
    const movedPaths = [];

    for (const file of files) {
        const rel = path.relative(root, file);

        if (isTextFile(file)) {
            const original = fs.readFileSync(file, "utf8");
            const { result, hits } = applyRules(original);
            if (hits > 0) {
                rewrittenFiles++;
                totalHits += hits;
                if (!dryRun) fs.writeFileSync(file, result, "utf8");
            }
        }

        const { path: newRel, changed } = renamePath(rel);
        if (changed) movedPaths.push([rel, newRel]);
    }

    // Deepest paths first, so renaming a parent directory never invalidates a child path.
    movedPaths.sort((a, b) => b[0].length - a[0].length);
    for (const [from, to] of movedPaths) {
        const fromAbs = path.join(root, from);
        const toAbs = path.join(root, to);
        if (!fs.existsSync(fromAbs)) continue;
        if (dryRun) {
            console.log(`  move  ${from}  ->  ${to}`);
        } else {
            movePath(fromAbs, toAbs);
        }
    }

    console.log("");
    console.log(`${dryRun ? "[dry run] " : ""}rewrote ${totalHits} occurrences across ${rewrittenFiles} files`);
    console.log(`${dryRun ? "[dry run] " : ""}renamed ${movedPaths.length} files or directories`);
}

main();
