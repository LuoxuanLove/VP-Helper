"""Copy declared NuGet license materials into a release package, without local cache paths."""
from pathlib import Path
import json
import shutil
import sys
import xml.etree.ElementTree as ET


def main() -> None:
    assets = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
    output = Path(sys.argv[2])
    output.mkdir(parents=True, exist_ok=True)
    folders = [Path(p) for p in assets["packageFolders"]]
    packages = {key: value["path"] for key, value in assets["libraries"].items() if value["type"] == "package"}
    for framework in assets.get("project", {}).get("frameworks", {}).values():
        for dep in framework.get("downloadDependencies", []):
            version = dep["version"].strip("[]")
            packages[f'{dep["name"]}/{version}'] = f'{dep["name"].lower()}/{version}'
    report = []
    for identity, relative in sorted(packages.items()):
        package = next((folder / relative for folder in folders if (folder / relative).is_dir()), None)
        if package is None:
            raise SystemExit(f"Package missing from restore cache: {identity}")
        nuspec = next(package.glob("*.nuspec"))
        metadata = ET.parse(nuspec).getroot().find("{*}metadata")
        license_node = metadata.find("{*}license")
        entry = {"package": identity, "licenseType": license_node.get("type") if license_node is not None else None,
                 "license": license_node.text if license_node is not None else None,
                 "licenseUrl": metadata.findtext("{*}licenseUrl"), "files": []}
        candidates = {p for p in package.rglob('*') if p.is_file() and p.name.lower().startswith(('license', 'notice', 'third-party', 'thirdparty', 'copying'))}
        if license_node is not None and license_node.get("type") == "file":
            license_path = (package / license_node.text).resolve()
            if not license_path.is_relative_to(package.resolve()) or not license_path.is_file():
                raise SystemExit(f"Missing or invalid declared license: {identity}")
            candidates.add(license_path)
        for source in sorted(candidates):
            rel = source.resolve().relative_to(package.resolve())
            destination = output / identity.replace('/', '-') / rel
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            entry["files"].append(destination.relative_to(output).as_posix())
        report.append(entry)
    (output / "dependencies.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding="utf-8")
    print(f"Collected license declarations for {len(report)} packages.")


if __name__ == "__main__":
    main()
