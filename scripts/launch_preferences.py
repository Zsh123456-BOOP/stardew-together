"""Presentation defaults for an isolated development configuration root."""
from pathlib import Path
import xml.etree.ElementTree as ET


def prepare_presentation(config_root: Path, width: int = 1280, height: int = 800) -> None:
    directory = config_root / "StardewValley"
    directory.mkdir(parents=True, exist_ok=True)

    def set_value(root, name, value):
        node = root.find(name)
        if node is None:
            node = ET.SubElement(root, name)
        node.text = str(value)

    def options(root):
        for field in ("fullscreen", "windowedBorderlessFullscreen"):
            set_value(root, field, "false")
        for field in ("musicVolumeLevel", "soundVolumeLevel", "footstepVolumeLevel", "ambientVolumeLevel"):
            set_value(root, field, 0)
        set_value(root, "preferredResolutionX", width)
        set_value(root, "preferredResolutionY", height)

    for name, tag in (("startup_preferences", "StartupPreferences"), ("default_options", "Options")):
        path = directory / name
        root = ET.parse(path).getroot() if path.exists() else ET.Element(tag)
        if name == "startup_preferences":
            set_value(root, "startMuted", "true")
            # Native StartupPreferences.windowed=1 (0 is borderless fullscreen).
            set_value(root, "windowMode", 1)
            set_value(root, "fullscreenResolutionX", width)
            set_value(root, "fullscreenResolutionY", height)
            client = root.find("clientOptions")
            if client is None:
                client = ET.SubElement(root, "clientOptions")
            options(client)
        else:
            options(root)
        temporary = path.with_name(path.name + ".next")
        ET.ElementTree(root).write(temporary, encoding="utf-8", xml_declaration=True)
        temporary.replace(path)
