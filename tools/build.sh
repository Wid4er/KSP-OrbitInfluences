#!/bin/sh
set -eu

project_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
ksp_root=${KSP_ROOT:-/home/daniel/.local/share/Steam/steamapps/common/Kerbal Space Program}
managed="$ksp_root/KSP_x64_Data/Managed"
dotnet_bin=${DOTNET_BIN:-dotnet}

if ! command -v "$dotnet_bin" >/dev/null 2>&1; then
    dotnet_bin=/home/daniel/Beelink/.cache/helios-dotnet/dotnet
fi

sdk_line=$("$dotnet_bin" --list-sdks | tail -n 1)
sdk_version=${sdk_line%% *}
sdk_dir=${sdk_line##*[}
sdk_dir=${sdk_dir%]}
compiler="$sdk_dir/$sdk_version/Roslyn/bincore/csc.dll"
output="$project_dir/GameData/OrbitInfluences/Plugins/OrbitInfluences.dll"

for dependency in mscorlib.dll System.dll Assembly-CSharp.dll UnityEngine.dll UnityEngine.CoreModule.dll; do
    if [ ! -f "$managed/$dependency" ]; then
        echo "Missing KSP dependency: $managed/$dependency" >&2
        exit 1
    fi
done

"$dotnet_bin" "$compiler" -nologo -noconfig -nostdlib+ -langversion:7.3 \
    -target:library -optimize+ -out:"$output" \
    -r:"$managed/mscorlib.dll" -r:"$managed/System.dll" \
    -r:"$managed/Assembly-CSharp.dll" \
    -r:"$managed/UnityEngine.dll" -r:"$managed/UnityEngine.CoreModule.dll" \
    -r:"$managed/UnityEngine.IMGUIModule.dll" \
    -r:"$managed/UnityEngine.AnimationModule.dll" \
    "$project_dir/source/SoiSettings.cs" \
    "$project_dir/source/SoiBoundaryRenderer.cs" \
    "$project_dir/source/SoiGridRenderer.cs" \
    "$project_dir/source/SoiTransitionMarker.cs" \
    "$project_dir/source/SoiSurfaceProjection.cs" \
    "$project_dir/source/SoiEncounterLookup.cs" \
    "$project_dir/source/SoiRenderedEscapeLookup.cs" \
    "$project_dir/source/SoiVisualizer.cs" \
    "$project_dir/source/SoiControlPanel.cs"

echo "Built $output"
