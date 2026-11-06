#!/bin/sh
# Regenerate every .axaml from the WinForms designers (never hand-edit the generated files).
set -e
here=$(cd "$(dirname "$0")" && pwd)
src="$here/../../TeconMoon's WiiVC Injector"
dst="$here/../WiiVCInjector"
gen="python3 $here/gen_axaml.py"
$gen "$src/Form1.Designer.cs" "$dst/MainWindow.axaml" /tmp/wiivc_h1.txt --resx "$src/Form1.resx"
$gen "$src/SettingsForm.Designer.cs" "$dst/SettingsWindow.axaml" /tmp/wiivc_h2.txt --cls WiiVCInjector.SettingsWindow --root this
$gen "$src/Form2.Designer.cs" "$dst/SDCardWindow.axaml" /tmp/wiivc_h3.txt --cls WiiVCInjector.SDCardWindow --root this
