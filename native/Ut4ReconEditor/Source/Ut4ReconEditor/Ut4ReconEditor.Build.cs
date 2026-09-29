using UnrealBuildTool;

public class Ut4ReconEditor : ModuleRules
{
    public Ut4ReconEditor(TargetInfo Target)
    {
        PrivateDependencyModuleNames.AddRange(new[]
        {
            "Core", "CoreUObject", "Engine", "InputCore", "Slate", "SlateCore", "Json",
            "Projects", "UnrealEd", "WorkspaceMenuStructure", "AssetTools"
        });
    }
}
