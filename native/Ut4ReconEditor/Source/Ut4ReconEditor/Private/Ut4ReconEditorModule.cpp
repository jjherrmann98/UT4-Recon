#include "Modules/ModuleManager.h"
#include "UObject/ObjectMacros.h"

// The archived UT4 source snapshot does not contain UHT output. This module uses
// no reflected types; suppress reflection declarations while parsing the Slate
// public headers. These macros add no instance data to the value types used here.
#undef GENERATED_BODY
#undef GENERATED_USTRUCT_BODY
#undef GENERATED_UCLASS_BODY
#undef GENERATED_IINTERFACE_BODY
#undef GENERATED_UINTERFACE_BODY
#undef UCLASS
#undef UINTERFACE
#define GENERATED_BODY(...)
#define GENERATED_USTRUCT_BODY(...)
#define GENERATED_UCLASS_BODY(...)
#define GENERATED_IINTERFACE_BODY(...)
#define GENERATED_UINTERFACE_BODY(...)
#define UCLASS(...)
#define UINTERFACE(...)

#include "Framework/Docking/TabManager.h"
#include "AssetToolsModule.h"
#include "IAssetTools.h"
#include "AutomatedAssetImportData.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformTime.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/PackageName.h"
#include "UObject/Package.h"
#include "UObject/UObjectGlobals.h"
#include "Misc/Parse.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Widgets/Docking/SDockTab.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Input/SCheckBox.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Images/SThrobber.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/Text/STextBlock.h"
#include "WorkspaceMenuStructure.h"
#include "WorkspaceMenuStructureModule.h"

#define LOCTEXT_NAMESPACE "Ut4ReconEditor"

DEFINE_LOG_CATEGORY_STATIC(LogUt4ReconEditor, Log, All);

namespace
{
    const FName Ut4ReconTabName(TEXT("Ut4Recon"));

    FString DisplayFidelity(const FString& Value)
    {
        if (Value == TEXT("exactEditable")) return TEXT("Exact editable");
        if (Value == TEXT("reconstructed")) return TEXT("Reconstructed");
        if (Value == TEXT("behavioralProxy")) return TEXT("Behavioral proxy");
        return TEXT("Context only");
    }

    TSharedRef<STextBlock> TextLine(const FString& Value, const FSlateColor& Color = FSlateColor::UseForeground())
    {
        return SNew(STextBlock).Text(FText::FromString(Value)).ColorAndOpacity(Color).AutoWrapText(true);
    }

    void AddHeading(const TSharedRef<SVerticalBox>& Box, const FString& Value)
    {
        Box->AddSlot().AutoHeight().Padding(0, 10, 0, 3)
        [SNew(STextBlock).Text(FText::FromString(Value)).Font(FCoreStyle::Get().GetFontStyle("NotificationList.FontBold"))];
    }

    void AddLine(const TSharedRef<SVerticalBox>& Box, const FString& Value, const FSlateColor& Color = FSlateColor::UseForeground())
    {
        Box->AddSlot().AutoHeight().Padding(0, 1)[TextLine(Value, Color)];
    }

    FString JsonString(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, const FString& Fallback = TEXT("Unavailable"))
    {
        FString Result;
        return Object.IsValid() && Object->TryGetStringField(Field, Result) ? Result : Fallback;
    }
}

class SUt4ReconPanel final : public SCompoundWidget
{
public:
    SLATE_BEGIN_ARGS(SUt4ReconPanel) {}
    SLATE_END_ARGS()

    void Construct(const FArguments&)
    {
        WorkspaceRoot = FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::GameDir(), TEXT("..")));
        FPaths::CollapseRelativeDirectories(WorkspaceRoot);
        ActiveEditorRoot = FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::EngineDir(), TEXT("..")));
        FPaths::CollapseRelativeDirectories(ActiveEditorRoot);
        WorkspaceManifestPath = FPaths::Combine(WorkspaceRoot, TEXT("editor-workspace.json"));
        BuildSettingsPath = FPaths::Combine(WorkspaceRoot, TEXT("ut4recon-editor-state.json"));
        ProxyScenePath = FPaths::Combine(WorkspaceRoot, TEXT("proxy-scene.json"));
        SupportReportPath = FPaths::Combine(WorkspaceRoot, TEXT("workspace-support-report.json"));
        FilterVisibility.Add(TEXT("EXACTEDITABLE"), true);
        FilterVisibility.Add(TEXT("RECONSTRUCTED"), true);
        FilterVisibility.Add(TEXT("BEHAVIORALPROXY"), true);
        FilterVisibility.Add(TEXT("PRESERVEONLY"), true);
        FilterVisibility.Add(TEXT("COLLISION_OVERLAY"), true);
        FilterVisibility.Add(TEXT("BSP_CONTEXT"), true);
        FilterVisibility.Add(TEXT("WORKSPACE_LIGHTING"), true);
        bFilterProbePending = FParse::Param(FCommandLine::Get(), TEXT("UT4ReconFilterProbe"));
        bActionProbe = FParse::Param(FCommandLine::Get(), TEXT("UT4ReconActionProbe"));
        bPreviewImportProbePending = FParse::Param(FCommandLine::Get(), TEXT("UT4ReconPreviewImportProbe"));
        FParse::Value(FCommandLine::Get(), TEXT("UT4ReconActionProbe="), ActionProbeMode);
        if (!ActionProbeMode.IsEmpty()) bActionProbe = true;
        LoadWorkspace();

        ChildSlot
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot().AutoHeight().Padding(6)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
                [SNew(SButton).Text(LOCTEXT("MapButton", "Map")).OnClicked(this, &SUt4ReconPanel::ShowMap)]
                + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
                [SNew(SButton).Text(LOCTEXT("SelectionButton", "Selected object")).OnClicked(this, &SUt4ReconPanel::ShowSelection)]
                + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
                [SNew(SButton).Text(LOCTEXT("BuildButton", "Build")).OnClicked(this, &SUt4ReconPanel::ShowBuild)]
                + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
                [SNew(SButton).Text(LOCTEXT("AdvancedButton", "Advanced")).OnClicked(this, &SUt4ReconPanel::ShowAdvanced)]
                + SHorizontalBox::Slot().FillWidth(1.0f)
                + SHorizontalBox::Slot().AutoWidth()
                [SNew(SButton).Text(LOCTEXT("ReloadButton", "Reload workspace")).OnClicked(this, &SUt4ReconPanel::Reload)]
            ]
            + SVerticalBox::Slot().AutoHeight().Padding(6, 0, 6, 6)
            [SAssignNew(ActivityBorder, SBorder).Padding(FMargin(8, 6))]
            + SVerticalBox::Slot().FillHeight(1.0f).Padding(6, 0, 6, 6)
            [SAssignNew(ContentBorder, SBorder).Padding(10)]
        ];

        ResolveEditorSelectionApi();
        RegisterActiveTimer(0.25f, FWidgetActiveTimerDelegate::CreateSP(this, &SUt4ReconPanel::PollSelection));
        ShowMap();
        if (bActionProbe)
        {
            ShowBuild();
            if (ActionProbeMode == TEXT("export")) ExportSavedMap();
            else if (ActionProbeMode == TEXT("build")) BuildPak();
            else StartBackendAction(TEXT("Validate editor changes"), { TEXT("export-editor-edits"), RecoveryProject, WorkspaceRoot });
        }
    }

    ~SUt4ReconPanel()
    {
        if (BackendProcess.IsValid()) FPlatformProcess::CloseProc(BackendProcess);
        CloseBackendPipes();
        if (UnrealEdHandle) FPlatformProcess::FreeDllHandle(UnrealEdHandle);
        if (EngineHandle) FPlatformProcess::FreeDllHandle(EngineHandle);
        if (CoreUObjectHandle) FPlatformProcess::FreeDllHandle(CoreUObjectHandle);
    }

private:
    enum class EView { Map, Selection, Build, Advanced };

    FReply ShowMap() { CurrentView = EView::Map; Rebuild(); return FReply::Handled(); }
    FReply ShowSelection() { CurrentView = EView::Selection; Rebuild(); return FReply::Handled(); }
    FReply ShowBuild() { CurrentView = EView::Build; Rebuild(); return FReply::Handled(); }
    FReply ShowAdvanced() { CurrentView = EView::Advanced; Rebuild(); return FReply::Handled(); }
    FReply Reload() { LoadWorkspace(); Rebuild(); return FReply::Handled(); }
    void ToggleSelectionDetails(ECheckBoxState State) { bShowSelectionDetails = State == ECheckBoxState::Checked; Rebuild(); }
    void ToggleBuildDetails(ECheckBoxState State) { bShowBuildDetails = State == ECheckBoxState::Checked; Rebuild(); }
    void MapNameChanged(const FText& Value) { SelectedMapName = Value.ToString(); }
    void OutputPakChanged(const FText& Value) { SelectedOutputPak = Value.ToString(); }
    void MapNameCommitted(const FText& Value, ETextCommit::Type) { SelectedMapName = Value.ToString(); SaveBuildSettings(); }
    void OutputPakCommitted(const FText& Value, ETextCommit::Type) { SelectedOutputPak = Value.ToString(); SaveBuildSettings(); }

    EActiveTimerReturnType PollSelection(double CurrentTime, float)
    {
        PollBackendProcess();
        if (bPreviewImportProbePending && CurrentTime - LastPreviewProbeAttemptTime >= 15.0)
        {
            LastPreviewProbeAttemptTime = CurrentTime; bPreviewImportProbePending = false; ImportMeshPreviews();
        }
        if (bFilterProbePending && Scene.IsValid() && CurrentTime - LastFilterProbeAttemptTime >= 5.0)
        {
            LastFilterProbeAttemptTime = CurrentTime;
            FilterVisibility.Add(TEXT("PRESERVEONLY"), false);
            const int32 Matched = ApplyViewportFilters();
            FilterVisibility.Add(TEXT("PRESERVEONLY"), true);
            if (Matched > 0)
            {
                ApplyViewportFilters();
                if (ProbeActor && GEditorAddress && *GEditorAddress && SelectActor)
                {
                    SelectActor(*GEditorAddress, ProbeActor, true, true, true, true);
                    const FString SelectedLabel = GetSelectedActorLabel();
                    const bool Mapped = ResolveObjectForLabel(SelectedLabel).IsValid();
                    UE_LOG(LogUt4ReconEditor, Log, TEXT("Selection certification probe selected '%s'; inventory mapping: %s."),
                        SelectedLabel.IsEmpty() ? TEXT("<none>") : *SelectedLabel, Mapped ? TEXT("passed") : TEXT("failed"));
                }
                bFilterProbePending = false;
                UE_LOG(LogUt4ReconEditor, Log, TEXT("Viewport-filter certification probe completed and restored all categories."));
            }
        }
        const FString Label = GetSelectedActorLabel();
        if (Label != LastSelectedActorLabel)
        {
            LastSelectedActorLabel = Label;
            UE_LOG(LogUt4ReconEditor, Log, TEXT("Selected editor actor: %s"), Label.IsEmpty() ? TEXT("<none>") : *Label);
            if (CurrentView == EView::Selection) Rebuild();
        }
        return EActiveTimerReturnType::Continue;
    }

    void ResolveEditorSelectionApi()
    {
        const FString BinaryRoot = FPaths::EngineDir() / TEXT("Binaries/Win64");
        UnrealEdHandle = FPlatformProcess::GetDllHandle(*(BinaryRoot / TEXT("UE4Editor-UnrealEd.dll")));
        EngineHandle = FPlatformProcess::GetDllHandle(*(BinaryRoot / TEXT("UE4Editor-Engine.dll")));
        CoreUObjectHandle = FPlatformProcess::GetDllHandle(*(BinaryRoot / TEXT("UE4Editor-CoreUObject.dll")));
        if (!UnrealEdHandle || !EngineHandle || !CoreUObjectHandle)
        {
            SelectionApiError = TEXT("The installed editor modules required for selection inspection could not be opened."); return;
        }
        GEditorAddress = reinterpret_cast<void**>(FPlatformProcess::GetDllExport(UnrealEdHandle, TEXT("?GEditor@@3PEAVUEditorEngine@@EA")));
        GetSelectedActors = reinterpret_cast<FGetSelectedActors>(FPlatformProcess::GetDllExport(UnrealEdHandle, TEXT("?GetSelectedActors@UEditorEngine@@QEBAPEAVUSelection@@XZ")));
        GetTop = reinterpret_cast<FGetTop>(FPlatformProcess::GetDllExport(EngineHandle, TEXT("?GetTop@USelection@@QEAAPEAVUObject@@PEAVUClass@@0_N@Z")));
        GetActorLabel = reinterpret_cast<FGetActorLabel>(FPlatformProcess::GetDllExport(EngineHandle, TEXT("?GetActorLabel@AActor@@QEBAAEBVFString@@XZ")));
        GetUObjectStaticClass = reinterpret_cast<FGetStaticClass>(FPlatformProcess::GetDllExport(CoreUObjectHandle, TEXT("?StaticClass@UObject@@SAPEAVUClass@@XZ")));
        GetActorStaticClass = reinterpret_cast<FGetStaticClass>(FPlatformProcess::GetDllExport(EngineHandle, TEXT("?StaticClass@AActor@@SAPEAVUClass@@XZ")));
        GetObjectsOfClass = reinterpret_cast<FGetObjectsOfClass>(FPlatformProcess::GetDllExport(CoreUObjectHandle, TEXT("?GetObjectsOfClass@@YAXPEAVUClass@@AEAV?$TArray@PEAVUObject@@VFDefaultAllocator@@@@_NW4EObjectFlags@@W4EInternalObjectFlags@@@Z")));
        SetActorHidden = reinterpret_cast<FSetActorHidden>(FPlatformProcess::GetDllExport(EngineHandle, TEXT("?SetIsTemporarilyHiddenInEditor@AActor@@UEAAX_N@Z")));
        IsActorHidden = reinterpret_cast<FIsActorHidden>(FPlatformProcess::GetDllExport(EngineHandle, TEXT("?IsTemporarilyHiddenInEditor@AActor@@QEBA_N_N@Z")));
        RedrawAllViewports = reinterpret_cast<FRedrawAllViewports>(FPlatformProcess::GetDllExport(UnrealEdHandle, TEXT("?RedrawAllViewports@UEditorEngine@@QEAAX_N@Z")));
        SelectActor = reinterpret_cast<FSelectActor>(FPlatformProcess::GetDllExport(UnrealEdHandle, TEXT("?SelectActor@UUnrealEdEngine@@UEAAXPEAVAActor@@_N111@Z")));
        if (!GEditorAddress || !GetSelectedActors || !GetTop || !GetActorLabel || !GetUObjectStaticClass ||
            !GetActorStaticClass || !GetObjectsOfClass || !SetActorHidden || !IsActorHidden || !RedrawAllViewports || !SelectActor)
            SelectionApiError = TEXT("The CL 3525360 selection ABI exports did not match the certified adapter profile.");
        else
            UE_LOG(LogUt4ReconEditor, Log, TEXT("Resolved the pinned CL 3525360 selection and viewport-filter ABI."));
    }

    FString GetSelectedActorLabel() const
    {
        if (!SelectionApiError.IsEmpty() || !GEditorAddress || !*GEditorAddress) return FString();
        void* Selection = GetSelectedActors(*GEditorAddress); if (!Selection) return FString();
        void* ObjectClass = GetUObjectStaticClass(); if (!ObjectClass) return FString();
        void* Actor = GetTop(Selection, ObjectClass, nullptr, false); if (!Actor) return FString();
        const FString* Label = GetActorLabel(Actor); return Label ? *Label : FString();
    }

    void LoadWorkspace()
    {
        Scene.Reset(); Visualization.Reset(); ObjectsById.Empty(); ObjectsByEditorName.Empty(); ObjectsByPath.Empty(); LoadError.Empty();
        TSharedPtr<FJsonObject> Workspace;
        if (!LoadJsonObject(WorkspaceManifestPath, Workspace))
        {
            LoadError = FString::Printf(TEXT("No valid editor-workspace.json was found at %s"), *WorkspaceManifestPath); return;
        }
        const int32 WorkspaceVersion = Workspace->GetIntegerField(TEXT("schemaVersion"));
        if (WorkspaceVersion != 3)
        {
            LoadError = FString::Printf(TEXT("Editor workspace schema %d is unsupported. Regenerate it with the current backend."), WorkspaceVersion); return;
        }
        RecoveryProject = JsonString(Workspace, TEXT("recoveryProject"), TEXT(""));
        BackendExecutable = JsonString(Workspace, TEXT("backendExecutable"), TEXT(""));
        DefaultOutputPak = JsonString(Workspace, TEXT("defaultOutputPak"), TEXT(""));
        DefaultMapName = JsonString(Workspace, TEXT("defaultMapName"), TEXT(""));
        SelectedOutputPak = DefaultOutputPak;
        SelectedMapName = DefaultMapName;
        LoadBuildSettings();
        ProjectFile = JsonString(Workspace, TEXT("projectFile"), TEXT(""));
        MapPackagePath = JsonString(Workspace, TEXT("mapPackagePath"), TEXT(""));
        WorkspaceExportPath = JsonString(Workspace, TEXT("exportT3d"), TEXT(""));
        WorkspaceMode = JsonString(Workspace, TEXT("workspaceMode"), TEXT("legacy-unspecified"));
        VisualizationIndexPath = JsonString(Workspace, TEXT("visualizationIndex"), TEXT(""));
        MeshPreviewImportPath = JsonString(Workspace, TEXT("meshPreviewImport"), TEXT(""));
        if (!VisualizationIndexPath.IsEmpty()) LoadJsonObject(VisualizationIndexPath, Visualization);
        BackendArguments.Empty();
        const TArray<TSharedPtr<FJsonValue>>* PrefixArguments = nullptr;
        if (Workspace->TryGetArrayField(TEXT("backendArguments"), PrefixArguments) && PrefixArguments)
            for (const TSharedPtr<FJsonValue>& Value : *PrefixArguments) BackendArguments.Add(Value->AsString());
        if (RecoveryProject.IsEmpty() || BackendExecutable.IsEmpty())
        {
            LoadError = TEXT("The workspace does not contain a usable recovery project and backend launch contract."); return;
        }
        if (OutputPakText.IsValid()) OutputPakText->SetText(FText::FromString(SelectedOutputPak));
        if (MapNameText.IsValid()) MapNameText->SetText(FText::FromString(SelectedMapName));
        FString Json;
        if (!FFileHelper::LoadFileToString(Json, *ProxyScenePath))
        {
            LoadError = FString::Printf(TEXT("No proxy-scene.json was found at %s"), *ProxyScenePath); return;
        }
        const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
        if (!FJsonSerializer::Deserialize(Reader, Scene) || !Scene.IsValid())
        {
            LoadError = FString::Printf(TEXT("proxy-scene.json could not be parsed: %s"), *ProxyScenePath); return;
        }
        const TArray<TSharedPtr<FJsonValue>>* Objects = nullptr;
        if (!Scene->TryGetArrayField(TEXT("objects"), Objects) || Objects == nullptr)
        {
            LoadError = TEXT("proxy-scene.json contains no objects array."); Scene.Reset(); return;
        }
        for (const TSharedPtr<FJsonValue>& Value : *Objects)
        {
            const TSharedPtr<FJsonObject> Object = Value->AsObject(); FString Id;
            if (Object.IsValid() && Object->TryGetStringField(TEXT("reconstructionId"), Id))
            {
                ObjectsById.Add(Id, Object);
                const FString EditorName = JsonString(Object, TEXT("editorName"), TEXT(""));
                const FString ObjectPath = JsonString(Object, TEXT("objectPath"), TEXT(""));
                if (!EditorName.IsEmpty() && !ObjectsByEditorName.Contains(EditorName)) ObjectsByEditorName.Add(EditorName, Object);
                if (!ObjectPath.IsEmpty()) ObjectsByPath.Add(ObjectPath, Object);
            }
        }
        UE_LOG(LogUt4ReconEditor, Log, TEXT("Loaded UT4 Recon workspace %s with %d indexed objects."), *WorkspaceRoot, ObjectsById.Num());
    }

    void Rebuild()
    {
        if (!ContentBorder.IsValid()) return;
        UpdateActivityStrip();
        if (!LoadError.IsEmpty())
        {
            ContentBorder->SetContent(SNew(SVerticalBox)
                + SVerticalBox::Slot().AutoHeight()[TextLine(TEXT("No active UT4 Recon workspace"), FLinearColor(1.0f, 0.35f, 0.35f))]
                + SVerticalBox::Slot().AutoHeight().Padding(0, 8)[TextLine(LoadError)]); return;
        }
        switch (CurrentView)
        {
        case EView::Map: ContentBorder->SetContent(BuildMapView()); break;
        case EView::Selection: ContentBorder->SetContent(BuildSelectionView()); break;
        case EView::Build: ContentBorder->SetContent(BuildBuildView()); break;
        case EView::Advanced: ContentBorder->SetContent(BuildAdvancedView()); break;
        }
    }

    void LoadBuildSettings()
    {
        TSharedPtr<FJsonObject> State;
        if (!LoadJsonObject(BuildSettingsPath, State)) return;
        SelectedMapName = JsonString(State, TEXT("mapName"), SelectedMapName);
        SelectedOutputPak = JsonString(State, TEXT("outputPak"), SelectedOutputPak);
    }

    void SaveBuildSettings() const
    {
        const TSharedRef<FJsonObject> State = MakeShareable(new FJsonObject);
        State->SetStringField(TEXT("mapName"), SelectedMapName);
        State->SetStringField(TEXT("outputPak"), SelectedOutputPak);
        FString Json;
        const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Json);
        if (FJsonSerializer::Serialize(State, Writer)) FFileHelper::SaveStringToFile(Json, *BuildSettingsPath);
    }

    FText ActivityText() const
    {
        if (BackendProcess.IsValid())
        {
            const int32 Seconds = FMath::Max(0, static_cast<int32>(FPlatformTime::Seconds() - ActionStartedAt));
            return FText::FromString(FString::Printf(TEXT("Working: %s (%d seconds). Editing actions are locked until this finishes."), *RunningAction, Seconds));
        }
        if (!ActionStatus.IsEmpty()) return FText::FromString(ActionStatus);
        return LOCTEXT("NoBackgroundAction", "Ready. No background action is running.");
    }

    void UpdateActivityStrip()
    {
        if (!ActivityBorder.IsValid()) return;
        const bool bRunning = BackendProcess.IsValid();
        const FSlateColor StatusColor = bRunning ? FLinearColor(0.35f, 0.75f, 1.0f)
            : bActionFailed ? FLinearColor(1.0f, 0.35f, 0.35f) : FLinearColor(0.35f, 0.9f, 0.45f);
        const TSharedRef<SHorizontalBox> Row = SNew(SHorizontalBox);
        if (bRunning)
            Row->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(0, 0, 8, 0)[SNew(SThrobber).NumPieces(3)];
        Row->AddSlot().FillWidth(1.0f).VAlign(VAlign_Center)
        [SNew(STextBlock).Text(this, &SUt4ReconPanel::ActivityText).ColorAndOpacity(StatusColor).AutoWrapText(true)];
        if (!BackendLogPath.IsEmpty() && FPaths::FileExists(BackendLogPath))
            Row->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(8, 0, 0, 0)
            [SNew(SButton).Text(LOCTEXT("ActivityLog", "View log")).OnClicked(this, &SUt4ReconPanel::OpenActionLog)];
        ActivityBorder->SetContent(Row);
    }

    TSharedRef<SWidget> BuildMapView()
    {
        const TSharedRef<SVerticalBox> Box = SNew(SVerticalBox);
        AddHeading(Box, TEXT("Map representation"));
        AddLine(Box, JsonString(Scene, TEXT("mapPackagePath")));
        AddLine(Box, FString::Printf(TEXT("Profile: %s"), *JsonString(Scene, TEXT("profile"))));
        AddLine(Box, FString::Printf(TEXT("Mode: %s"), *WorkspaceMode));
        if (WorkspaceMode == TEXT("experimental-runtime-proxies"))
            AddLine(Box, TEXT("Warning: active gameplay and engine actors are experimental and may make this workspace unstable."), FLinearColor(1.0f, 0.35f, 0.35f));
        AddLine(Box, FString::Printf(TEXT("Workspace: %s"), *WorkspaceRoot));
        AddLine(Box, FString::Printf(TEXT("Represented objects: %d"), ObjectsById.Num()));
        const TArray<TSharedPtr<FJsonValue>>* Counts = nullptr;
        if (Scene->TryGetArrayField(TEXT("fidelityCounts"), Counts) && Counts)
        {
            AddHeading(Box, TEXT("Fidelity"));
            for (const TSharedPtr<FJsonValue>& Value : *Counts)
            {
                const TSharedPtr<FJsonObject> Count = Value->AsObject(); const FString Category = JsonString(Count, TEXT("category"));
                const int32 Number = Count.IsValid() ? static_cast<int32>(Count->GetNumberField(TEXT("count"))) : 0;
                AddLine(Box, FString::Printf(TEXT("%s: %d"), *DisplayFidelity(Category), Number));
            }
        }
        AddHeading(Box, TEXT("Viewport visibility"));
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Exact editable"), TEXT("EXACTEDITABLE"))];
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Reconstructed"), TEXT("RECONSTRUCTED"))];
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Behavioral proxies"), TEXT("BEHAVIORALPROXY"))];
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Context only"), TEXT("PRESERVEONLY"))];
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Collision overlays"), TEXT("COLLISION_OVERLAY"))];
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Reconstructed BSP context"), TEXT("BSP_CONTEXT"))];
        Box->AddSlot().AutoHeight()[FilterRow(TEXT("Workspace lighting"), TEXT("WORKSPACE_LIGHTING"))];
        if (Visualization.IsValid())
        {
            AddHeading(Box, TEXT("Editor-only visualization"));
            AddLine(Box, FString::Printf(TEXT("BSP context: %.0f final cooked polygons"), Visualization->GetNumberField(TEXT("bspPolygonCount"))));
            AddLine(Box, FString::Printf(TEXT("Preview lights: %.0f"), Visualization->GetNumberField(TEXT("lightingActorCount"))));
            AddLine(Box, TEXT("These helpers improve navigation and are never inserted into the output pak."));
        }
        AddHeading(Box, TEXT("Recovered mesh previews"));
        if (MeshPreviewImportPath.IsEmpty() || !FPaths::FileExists(MeshPreviewImportPath))
            AddLine(Box, TEXT("No recovered mesh preview manifest is available."));
        else
        {
            int32 AvailablePreviews = 0, MissingPreviews = 0; CountMeshPreviewState(AvailablePreviews, MissingPreviews);
            AddLine(Box, FString::Printf(TEXT("Source packages available: %d; recovered previews still missing: %d."), AvailablePreviews, MissingPreviews));
            AddLine(Box, TEXT("Imports recovered LOD0 geometry under missing original asset paths using fixed automated static-mesh defaults. Existing source assets are skipped. Preview materials, authored LODs, sockets, and import metadata are not reconstructed."));
            Box->AddSlot().AutoHeight().Padding(0, 4)
            [SNew(SButton).Text(LOCTEXT("ImportMeshPreviews", "Import missing recovered mesh previews")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::ImportMeshPreviews)];
            AddLine(Box, TEXT("After importing, close the editor, run run-editor-import for this workspace, then reopen it so proxy actors resolve the new assets."), FLinearColor(0.9f, 0.7f, 0.2f));
        }
        if (!PreviewImportStatus.IsEmpty()) AddLine(Box, PreviewImportStatus, bPreviewImportFailed ? FLinearColor(1.0f, 0.35f, 0.35f) : FLinearColor(0.35f, 0.9f, 0.45f));
        if (!SelectionApiError.IsEmpty()) AddLine(Box, SelectionApiError, FLinearColor(1.0f, 0.35f, 0.35f));
        else AddLine(Box, TEXT("These controls temporarily hide generated workspace actors. They do not modify or export the map."));
        AddLine(Box, TEXT("Visibility does not imply editability. Select an object to see its exportable operations."));
        return SNew(SScrollBox) + SScrollBox::Slot()[Box];
    }

    TSharedRef<SWidget> FilterRow(const FString& Label, const FString& Token)
    {
        return SNew(SCheckBox).IsChecked(this, &SUt4ReconPanel::GetFilterState, Token)
            .IsEnabled(SelectionApiError.IsEmpty())
            .OnCheckStateChanged(this, &SUt4ReconPanel::OnFilterChanged, Token)
            .ToolTipText(FText::FromString(Token))[TextLine(Label)];
    }

    ECheckBoxState GetFilterState(FString Token) const
    {
        const bool* Visible = FilterVisibility.Find(Token);
        return Visible && *Visible ? ECheckBoxState::Checked : ECheckBoxState::Unchecked;
    }

    void OnFilterChanged(ECheckBoxState State, FString Token)
    {
        FilterVisibility.Add(Token, State == ECheckBoxState::Checked);
        ApplyViewportFilters();
    }

    FString FilterTokenForLabel(const FString& Label) const
    {
        if (Label.StartsWith(TEXT("[Exact collision overlay] "))) return TEXT("COLLISION_OVERLAY");
        if (Label.StartsWith(TEXT("[Reconstructed BSP context] "))) return TEXT("BSP_CONTEXT");
        if (Label.StartsWith(TEXT("[Workspace lighting] "))) return TEXT("WORKSPACE_LIGHTING");
        if (Label.StartsWith(TEXT("[Exact editable] "))) return TEXT("EXACTEDITABLE");
        if (Label.StartsWith(TEXT("[Reconstructed] "))) return TEXT("RECONSTRUCTED");
        if (Label.StartsWith(TEXT("[Behavioral proxy] "))) return TEXT("BEHAVIORALPROXY");
        if (Label.StartsWith(TEXT("[Context only] "))) return TEXT("PRESERVEONLY");
        return FString();
    }

    int32 ApplyViewportFilters()
    {
        if (!SelectionApiError.IsEmpty() || !GetActorStaticClass || !GetObjectsOfClass || !SetActorHidden) return 0;
        void* ActorClass = GetActorStaticClass();
        if (!ActorClass) return 0;
        TArray<void*> Actors;
        GetObjectsOfClass(ActorClass, Actors, true, 0x10u, 1 << 29);
        int32 Matched = 0;
        int32 StateMismatches = 0;
        int32 NonEmptyLabels = 0;
        FString FirstLabel;
        for (void* Actor : Actors)
        {
            const FString* Label = GetActorLabel(Actor);
            if (!Label) continue;
            if (!Label->IsEmpty())
            {
                ++NonEmptyLabels;
                if (FirstLabel.IsEmpty()) FirstLabel = *Label;
            }
            const FString Token = FilterTokenForLabel(*Label);
            if (Token.IsEmpty()) continue;
            if (!ProbeActor) ProbeActor = Actor;
            const bool* Visible = FilterVisibility.Find(Token);
            const bool ShouldHide = Visible && !*Visible;
            SetActorHidden(Actor, ShouldHide);
            if (IsActorHidden(Actor, false) != ShouldHide) ++StateMismatches;
            ++Matched;
        }
        if (GEditorAddress && *GEditorAddress && RedrawAllViewports) RedrawAllViewports(*GEditorAddress, false);
        UE_LOG(LogUt4ReconEditor, Log, TEXT("Enumerated %d actors (%d labeled; first label: %s). Applied filters to %d tagged actors with %d state mismatches."),
            Actors.Num(), NonEmptyLabels, FirstLabel.IsEmpty() ? TEXT("<none>") : *FirstLabel, Matched, StateMismatches);
        return Matched;
    }

    TSharedRef<SWidget> BuildSelectionView()
    {
        const TSharedRef<SVerticalBox> Box = SNew(SVerticalBox); AddHeading(Box, TEXT("Selected object"));
        if (!SelectionApiError.IsEmpty())
        {
            AddLine(Box, SelectionApiError, FLinearColor(1.0f, 0.35f, 0.35f));
            return SNew(SScrollBox) + SScrollBox::Slot()[Box];
        }
        const FString Label = LastSelectedActorLabel;
        if (Label.IsEmpty())
        {
            AddLine(Box, TEXT("Select a represented actor or collision overlay in the viewport or World Outliner."));
            return SNew(SScrollBox) + SScrollBox::Slot()[Box];
        }
        if (Label.StartsWith(TEXT("[Reconstructed BSP context] ")))
        {
            AddHeading(Box, TEXT("Recovered floor and wall context"));
            AddLine(Box, TEXT("This geometry helps you navigate the cooked map. Changes to it are not exported."));
            AddLine(Box, TEXT("You can: inspect and use it as spatial reference."), FLinearColor(0.35f, 0.9f, 0.45f));
            AddLine(Box, TEXT("Important limit: the original additive and subtractive brush history was not recoverable."), FLinearColor(0.9f, 0.7f, 0.2f));
            AddSelectionDetailsToggle(Box);
            if (bShowSelectionDetails)
            {
                AddHeading(Box, TEXT("Technical details"));
                AddLine(Box, FString::Printf(TEXT("Editor actor: %s"), *Label));
                AddLine(Box, TEXT("Representation: reconstructed final cooked BSP surfaces"));
                AddLine(Box, TEXT("Pak effect: none. This protected actor is excluded from edit export."));
                AddLine(Box, TEXT("A deliberate full-model replacement requires the isolated BSP donor workflow."));
            }
            return SNew(SScrollBox) + SScrollBox::Slot()[Box];
        }
        if (Label.StartsWith(TEXT("[Workspace lighting] ")))
        {
            AddHeading(Box, TEXT("Workspace lighting"));
            AddLine(Box, TEXT("This light only makes the recovered workspace easier to view. It is never added to the rebuilt map."));
            AddLine(Box, TEXT("You can: adjust it for your own editor view."), FLinearColor(0.35f, 0.9f, 0.45f));
            AddSelectionDetailsToggle(Box);
            if (bShowSelectionDetails)
            {
                AddHeading(Box, TEXT("Technical details"));
                AddLine(Box, FString::Printf(TEXT("Editor actor: %s"), *Label));
                AddLine(Box, TEXT("Representation: editor-only preview lighting"));
                AddLine(Box, TEXT("Pak effect: none. Light changes are excluded from edit export."));
            }
            return SNew(SScrollBox) + SScrollBox::Slot()[Box];
        }
        const bool IsOverlay = Label.StartsWith(TEXT("[Exact collision overlay] "));
        TSharedPtr<FJsonObject> Object = ResolveObjectForLabel(Label);
        if (!Object.IsValid())
        {
            AddLine(Box, TEXT("This actor is not mapped to the active reconstruction inventory."), FLinearColor(1.0f, 0.35f, 0.35f));
            return SNew(SScrollBox) + SScrollBox::Slot()[Box];
        }
        const FString Fidelity = JsonString(Object, TEXT("fidelity"));
        const FString SelectedId = JsonString(Object, TEXT("reconstructionId"), TEXT(""));
        TArray<FString> FriendlyActions;
        AppendFriendlyActions(Object, FriendlyActions);
        if (!SelectedId.IsEmpty())
            for (const TPair<FString, TSharedPtr<FJsonObject>>& Pair : ObjectsById)
                if (Pair.Value.IsValid() && JsonString(Pair.Value, TEXT("ownerReconstructionId"), TEXT("")) == SelectedId)
                    AppendFriendlyActions(Pair.Value, FriendlyActions);

        FString ObjectType = DisplayFidelity(Fidelity) + TEXT(" object");
        FString ImportantLimit = TEXT("Only the supported changes listed here are exported; all other cooked data is preserved.");
        if (IsOverlay)
        {
            ObjectType = TEXT("Collision blocker");
            FriendlyActions.AddUnique(TEXT("Delete"));
            ImportantLimit = TEXT("The visible overlay is a guide. Moving, rotating, duplicating, or deleting it changes the cooked BlockingVolume; its brush vertices cannot be remodeled.");
        }
        else if (Label.Contains(TEXT("Player start marker")))
        {
            ObjectType = TEXT("Player start");
            ImportantLimit = TEXT("Team assignment, marker shape, collision, creation, deletion, and spawn behavior are preserved.");
        }
        else if (Label.Contains(TEXT("Pickup marker")))
        {
            ObjectType = TEXT("Pickup");
            ImportantLimit = TEXT("Item type, respawn settings, collision shape, creation, deletion, and runtime behavior are preserved.");
        }
        else if (JsonString(Object, TEXT("classPath"), TEXT("")).Contains(TEXT("StaticMeshActor")))
        {
            ObjectType = TEXT("Static mesh actor");
            ImportantLimit = TEXT("The actor can use the supported changes below. Its referenced mesh asset, materials, and built-in collision remain preserved.");
        }
        AddHeading(Box, ObjectType);
        AddLine(Box, FriendlyActions.Num() == 0 ? TEXT("You can: inspect this representation. No editor change is exportable.")
            : TEXT("You can: ") + FString::Join(FriendlyActions, TEXT(", ")) + TEXT("."), FriendlyActions.Num() == 0 ? FLinearColor(0.9f, 0.7f, 0.2f) : FLinearColor(0.35f, 0.9f, 0.45f));
        AddLine(Box, IsOverlay ? TEXT("Supported changes are applied to the original cooked BlockingVolume during the build.")
            : TEXT("Supported changes are applied to the corresponding original cooked object during the build."));
        AddLine(Box, TEXT("Important limit: ") + ImportantLimit, FLinearColor(0.9f, 0.7f, 0.2f));
        AddSelectionDetailsToggle(Box);
        if (!bShowSelectionDetails) return SNew(SScrollBox) + SScrollBox::Slot()[Box];

        AddHeading(Box, TEXT("Technical details"));
        AddLine(Box, FString::Printf(TEXT("Editor actor: %s"), *Label));
        AddLine(Box, FString::Printf(TEXT("Runtime target: %s"), *JsonString(Object, TEXT("objectPath"))));
        AddLine(Box, FString::Printf(TEXT("Runtime class: %s"), *JsonString(Object, TEXT("classPath"))));
        AddLine(Box, FString::Printf(TEXT("Representation: %s%s"), *DisplayFidelity(Fidelity), IsOverlay ? TEXT(" collision visualization") : TEXT("")));
        if (IsOverlay) AddLine(Box, TEXT("Pak effect: the overlay itself is never packaged; supported workspace changes are translated to the original cooked BlockingVolume."), FLinearColor(0.9f, 0.7f, 0.2f));
        else AddLine(Box, TEXT("Pak effect: only operations listed under 'Editable in this workspace' are captured from the editor. Other cooked bytes remain authoritative."), FLinearColor(0.9f, 0.7f, 0.2f));

        const TSharedPtr<FJsonObject>* Collision = nullptr;
        if (Object->TryGetObjectField(TEXT("collision"), Collision) && Collision && Collision->IsValid())
        {
            AddHeading(Box, TEXT("Collision"));
            AddLine(Box, FString::Printf(TEXT("Source: %s"), *JsonString(*Collision, TEXT("sourceKind"))));
            AddLine(Box, FString::Printf(TEXT("Geometry fidelity: %s"), *JsonString(*Collision, TEXT("geometryFidelity"))));
            AddLine(Box, JsonString(*Collision, TEXT("limitation"), TEXT("No collision limitation recorded.")));
        }

        const TArray<TSharedPtr<FJsonValue>>* Capabilities = nullptr;
        if (Object->TryGetArrayField(TEXT("capabilities"), Capabilities) && Capabilities)
        {
            AddCapabilityGroup(Box, *Capabilities, TEXT("Editable in this workspace"), TEXT("workspace"));
            AddCapabilityGroup(Box, *Capabilities, TEXT("Available through direct/external operation"), TEXT("direct"));
            AddCapabilityGroup(Box, *Capabilities, TEXT("Preserved by the cooked-baseline build"), TEXT("preserved"));
            AddCapabilityGroup(Box, *Capabilities, TEXT("Requires a donor workflow"), TEXT("donor"));
            AddCapabilityGroup(Box, *Capabilities, TEXT("Unsupported operations"), TEXT("blocked"));
        }
        if (!SelectedId.IsEmpty())
        {
            for (const TPair<FString, TSharedPtr<FJsonObject>>& Pair : ObjectsById)
            {
                const TSharedPtr<FJsonObject>& Owned = Pair.Value;
                if (!Owned.IsValid() || JsonString(Owned, TEXT("ownerReconstructionId"), TEXT("")) != SelectedId) continue;
                const TArray<TSharedPtr<FJsonValue>>* OwnedCapabilities = nullptr;
                if (!Owned->TryGetArrayField(TEXT("capabilities"), OwnedCapabilities) || !OwnedCapabilities) continue;
                bool HasWorkspaceCapability = false;
                for (const TSharedPtr<FJsonValue>& Value : *OwnedCapabilities)
                {
                    const TSharedPtr<FJsonObject> Capability = Value->AsObject();
                    if (Capability.IsValid() && CapabilityGroup(Capability) == TEXT("workspace")) { HasWorkspaceCapability = true; break; }
                }
                if (!HasWorkspaceCapability) continue;
                AddHeading(Box, FString::Printf(TEXT("Editable component: %s"), *JsonString(Owned, TEXT("editorName"), TEXT("component"))));
                AddLine(Box, FString::Printf(TEXT("Runtime target: %s"), *JsonString(Owned, TEXT("objectPath"))));
                AddCapabilityGroup(Box, *OwnedCapabilities, TEXT("Editable in this workspace"), TEXT("workspace"));
                AddLine(Box, TEXT("Use the selected actor's Transform controls; validated values are written to this cooked component."), FLinearColor(0.35f, 0.9f, 0.45f));
                AddStringArray(Box, Owned, TEXT("limitations"), TEXT("Component limitations"));
            }
        }
        AddStringArray(Box, Object, TEXT("limitations"), TEXT("Limitations"));
        AddStringArray(Box, Object, TEXT("evidence"), TEXT("Recovered evidence"));
        return SNew(SScrollBox) + SScrollBox::Slot()[Box];
    }

    void AddSelectionDetailsToggle(const TSharedRef<SVerticalBox>& Box)
    {
        Box->AddSlot().AutoHeight().Padding(0, 8, 0, 2)
        [SNew(SCheckBox).IsChecked(bShowSelectionDetails ? ECheckBoxState::Checked : ECheckBoxState::Unchecked)
            .OnCheckStateChanged(this, &SUt4ReconPanel::ToggleSelectionDetails)
            [SNew(STextBlock).Text(LOCTEXT("SelectionDetails", "Show technical details"))]];
    }

    void AppendFriendlyActions(const TSharedPtr<FJsonObject>& Object, TArray<FString>& Actions) const
    {
        const TArray<TSharedPtr<FJsonValue>>* Capabilities = nullptr;
        if (!Object.IsValid() || !Object->TryGetArrayField(TEXT("capabilities"), Capabilities) || !Capabilities) return;
        for (const TSharedPtr<FJsonValue>& Value : *Capabilities)
        {
            const TSharedPtr<FJsonObject> Capability = Value->AsObject();
            if (!Capability.IsValid() || CapabilityGroup(Capability) != TEXT("workspace")) continue;
            const FString Operation = JsonString(Capability, TEXT("operation"), TEXT(""));
            const FString Property = JsonString(Capability, TEXT("propertyPath"), TEXT(""));
            FString Friendly;
            if (Operation == TEXT("set-property") && Property == TEXT("RelativeLocation")) Friendly = TEXT("Move");
            else if (Operation == TEXT("set-property") && Property == TEXT("RelativeRotation")) Friendly = TEXT("Rotate");
            else if (Operation == TEXT("set-property") && Property == TEXT("RelativeScale3D")) Friendly = TEXT("Scale");
            else if (Operation == TEXT("delete-actor") || Operation == TEXT("delete-collision-overlay")) Friendly = TEXT("Delete");
            else if (Operation.Contains(TEXT("clone")) || Operation.Contains(TEXT("duplicate"))) Friendly = TEXT("Duplicate");
            else if (Operation == TEXT("edit-collision-overlay-transform")) Friendly = TEXT("Move or rotate");
            if (Friendly == TEXT("Move or rotate"))
            {
                Actions.Remove(TEXT("Move")); Actions.Remove(TEXT("Rotate")); Actions.AddUnique(Friendly);
            }
            else if (!Friendly.IsEmpty() && !(Actions.Contains(TEXT("Move or rotate")) && (Friendly == TEXT("Move") || Friendly == TEXT("Rotate"))))
                Actions.AddUnique(Friendly);
        }
    }

    TSharedPtr<FJsonObject> ResolveObjectForLabel(const FString& Label) const
    {
        const FString OverlayPrefix = TEXT("[Exact collision overlay] ");
        if (Label.StartsWith(OverlayPrefix))
        {
            if (const TSharedPtr<FJsonObject>* Match = ObjectsByPath.Find(Label.Mid(OverlayPrefix.Len()))) return *Match;
            return nullptr;
        }
        FString EditorName = Label;
        int32 Separator = INDEX_NONE;
        if (Label.FindChar(TEXT(']'), Separator) && Label.IsValidIndex(Separator + 1))
        {
            EditorName = Label.Mid(Separator + 1);
            while (EditorName.StartsWith(TEXT(" "))) EditorName = EditorName.Mid(1);
        }
        if (const TSharedPtr<FJsonObject>* Match = ObjectsByEditorName.Find(EditorName)) return *Match;
        return nullptr;
    }

    void AddStringArray(const TSharedRef<SVerticalBox>& Box, const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, const TCHAR* Heading)
    {
        const TArray<TSharedPtr<FJsonValue>>* Values = nullptr;
        if (!Object->TryGetArrayField(Field, Values) || !Values || Values->Num() == 0) return;
        AddHeading(Box, Heading); for (const TSharedPtr<FJsonValue>& Value : *Values) AddLine(Box, TEXT("- ") + Value->AsString());
    }

    FString CapabilityGroup(const TSharedPtr<FJsonObject>& Capability) const
    {
        const FString State = JsonString(Capability, TEXT("state"), TEXT("blocked"));
        const FString Interface = JsonString(Capability, TEXT("interface"), TEXT(""));
        if (State == TEXT("blocked")) return TEXT("blocked");
        if (State == TEXT("requiresDonor") || Interface.Contains(TEXT("donor"))) return TEXT("donor");
        if (Interface == TEXT("editor-protocol-v2")) return TEXT("workspace");
        if (Interface == TEXT("cli-manifest")) return TEXT("direct");
        if (Interface == TEXT("build")) return TEXT("preserved");
        return TEXT("direct");
    }

    void AddCapabilityGroup(const TSharedRef<SVerticalBox>& Box, const TArray<TSharedPtr<FJsonValue>>& Capabilities, const TCHAR* Heading, const TCHAR* Group)
    {
        TArray<TSharedPtr<FJsonObject>> Matches;
        for (const TSharedPtr<FJsonValue>& Value : Capabilities)
        {
            const TSharedPtr<FJsonObject> Capability = Value->AsObject();
            if (Capability.IsValid() && CapabilityGroup(Capability) == Group) Matches.Add(Capability);
        }
        if (Matches.Num() == 0) return;
        AddHeading(Box, Heading);
        for (const TSharedPtr<FJsonObject>& Capability : Matches)
        {
            FString Operation = JsonString(Capability, TEXT("operation"));
            const FString Property = JsonString(Capability, TEXT("propertyPath"), TEXT(""));
            if (!Property.IsEmpty()) Operation += TEXT(": ") + Property;
            const FString Reason = JsonString(Capability, TEXT("reason"), TEXT(""));
            AddLine(Box, (FCString::Strcmp(Group, TEXT("blocked")) == 0 ? TEXT("- ") : TEXT("+ ")) + Operation + (Reason.IsEmpty() ? TEXT("") : TEXT(" - ") + Reason));
        }
    }

    TSharedRef<SWidget> BuildBuildView()
    {
        const TSharedRef<SVerticalBox> Box = SNew(SVerticalBox); AddHeading(Box, TEXT("Build a repaired map"));
        AddLine(Box, TEXT("Follow these steps in order. The final pak starts from the original cooked map and applies only changes that pass validation."));
        AddLine(Box, TEXT("If you edit the map again, save it and repeat Export and Validate before building."), FLinearColor(0.9f, 0.7f, 0.2f));

        const FString MapFile = EditorMapFile();
        const bool bMapExists = !MapFile.IsEmpty() && FPaths::FileExists(MapFile);
        const bool bExportExists = FPaths::FileExists(WorkspaceExportPath);
        const bool bExportCurrent = bMapExists && bExportExists && IFileManager::Get().GetTimeStamp(*WorkspaceExportPath) >= IFileManager::Get().GetTimeStamp(*MapFile);
        const FString EditManifest = FPaths::Combine(RecoveryProject, TEXT(".ut4recon/edit-manifest.json"));
        const FString DiffReport = FPaths::Combine(RecoveryProject, TEXT(".ut4recon/reports/editor-diff.json"));
        bool bValidationPassed = false;
        TSharedPtr<FJsonObject> Validation;
        const bool bValidationFiles = FPaths::FileExists(EditManifest) && FPaths::FileExists(DiffReport);
        const bool bValidationCurrent = bExportCurrent && bValidationFiles &&
            IFileManager::Get().GetTimeStamp(*DiffReport) >= IFileManager::Get().GetTimeStamp(*WorkspaceExportPath) &&
            IFileManager::Get().GetTimeStamp(*EditManifest) >= IFileManager::Get().GetTimeStamp(*WorkspaceExportPath);
        if (bValidationCurrent && LoadJsonObject(DiffReport, Validation) && Validation->HasTypedField<EJson::Boolean>(TEXT("passed")))
            bValidationPassed = Validation->GetBoolField(TEXT("passed"));
        FString BuildReadiness;
        const bool bReadyToBuild = IsBuildInputCurrent(BuildReadiness);

        AddBuildStage(Box, TEXT("1"), TEXT("Save the map"), bMapExists ? TEXT("Use Ctrl+S after every edit.") : TEXT("Workspace map is missing."), bMapExists);
        AddBuildStage(Box, TEXT("2"), TEXT("Export the saved map"), bExportCurrent ? TEXT("Current export is ready.") : bExportExists ? TEXT("The map changed after the last export; export it again.") : TEXT("No export has been created yet."), bExportCurrent);
        Box->AddSlot().AutoHeight().Padding(0, 4)
        [
            SNew(SHorizontalBox)
            + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
            [SNew(SButton).Text(LOCTEXT("ExportSavedMap", "Export saved map")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::ExportSavedMap)]
        ];

        AddBuildStage(Box, TEXT("3"), TEXT("Validate supported changes"), bValidationPassed ? TEXT("Validation passed.")
            : bValidationCurrent ? TEXT("Validation failed; open the report and correct the listed changes.")
            : bExportCurrent ? TEXT("Ready to validate the current export.") : TEXT("Export the saved map first."), bValidationPassed);
        Box->AddSlot().AutoHeight().Padding(0, 4)
        [SNew(SButton).Text(LOCTEXT("ValidateChanges", "Validate changes")).IsEnabled(this, &SUt4ReconPanel::CanValidateChanges).OnClicked(this, &SUt4ReconPanel::ValidateChanges)];

        AddBuildStage(Box, TEXT("4"), TEXT("Choose a distinct map identity"), TEXT("Use a name that clearly distinguishes this build from the original map."), bValidationPassed);
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(MapNameText, SEditableTextBox).Text(FText::FromString(SelectedMapName)).HintText(LOCTEXT("MapNameHint", "Distinct map/package name"))
            .OnTextChanged(this, &SUt4ReconPanel::MapNameChanged).OnTextCommitted(this, &SUt4ReconPanel::MapNameCommitted)];
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(OutputPakText, SEditableTextBox).Text(FText::FromString(SelectedOutputPak)).HintText(LOCTEXT("OutputPakHint", "Output pak path"))
            .OnTextChanged(this, &SUt4ReconPanel::OutputPakChanged).OnTextCommitted(this, &SUt4ReconPanel::OutputPakCommitted)];

        AddBuildStage(Box, TEXT("5"), TEXT("Build the pak"), bReadyToBuild ? TEXT("Ready to build an integrity-tested pak.") : BuildReadiness, bReadyToBuild);
        Box->AddSlot().AutoHeight().Padding(0, 4)
        [SNew(SButton).Text(LOCTEXT("BuildPak", "Build integrity-tested pak")).IsEnabled(this, &SUt4ReconPanel::CanBuildPak).OnClicked(this, &SUt4ReconPanel::BuildPak)];

        AddBuildStage(Box, TEXT("6"), TEXT("Test in Unreal Tournament"), TEXT("After a successful build, install the pak, load its distinct map URL, and verify the intended gameplay change."), true);
        Box->AddSlot().AutoHeight().Padding(0, 8, 0, 2)
        [SNew(SCheckBox).IsChecked(bShowBuildDetails ? ECheckBoxState::Checked : ECheckBoxState::Unchecked)
            .OnCheckStateChanged(this, &SUt4ReconPanel::ToggleBuildDetails)
            [SNew(STextBlock).Text(LOCTEXT("BuildDetails", "Show technical build details"))]];
        if (bShowBuildDetails)
        {
            AddHeading(Box, TEXT("Technical build details"));
            AddLine(Box, FString::Printf(TEXT("Active workspace: %s"), *WorkspaceRoot));
            AddLine(Box, FString::Printf(TEXT("Recovery project: %s"), *RecoveryProject));
            AddLine(Box, FString::Printf(TEXT("Support report: %s"), FPaths::FileExists(SupportReportPath) ? TEXT("available") : TEXT("missing")));
            AddPendingOperations(Box, EditManifest);
            AddReportSummary(Box);
            Box->AddSlot().AutoHeight().Padding(0, 4)
            [SNew(SButton).Text(LOCTEXT("OpenReports", "Open technical reports")).OnClicked(this, &SUt4ReconPanel::OpenReports)];
        }
        return SNew(SScrollBox) + SScrollBox::Slot()[Box];
    }

    void AddBuildStage(const TSharedRef<SVerticalBox>& Box, const FString& Number, const FString& Title, const FString& Status, bool bReady)
    {
        AddHeading(Box, Number + TEXT(". ") + Title);
        AddLine(Box, (bReady ? TEXT("Ready: ") : TEXT("Needs attention: ")) + Status,
            bReady ? FLinearColor(0.35f, 0.9f, 0.45f) : FLinearColor(0.9f, 0.7f, 0.2f));
    }

    TSharedRef<SWidget> BuildAdvancedView()
    {
        const TSharedRef<SVerticalBox> Box = SNew(SVerticalBox); AddHeading(Box, TEXT("Advanced tools"));
        AddLine(Box, TEXT("Most collision fixes should duplicate an existing collision overlay or use a standard box. Use custom collision only when the required shape cannot be represented safely that way."), FLinearColor(0.9f, 0.7f, 0.2f));
        AddHeading(Box, TEXT("Import a custom collision shape"));
        AddLine(Box, TEXT("This creates an isolated one-BlockingVolume donor project. Edit only its brush, save and close it, then cook, certify, and add the closure to this map."));
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(CustomWorkspaceText, SEditableTextBox).Text(FText::FromString(FPaths::Combine(RecoveryProject, TEXT("CustomCollisionDonor")))).HintText(LOCTEXT("CustomWorkspaceHint", "Custom donor workspace"))];
        Box->AddSlot().AutoHeight().Padding(0, 3)
        [
            SNew(SHorizontalBox)
            + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
            [SNew(SButton).Text(LOCTEXT("CreateCustomDonor", "Create workspace")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::CreateCustomDonor)]
            + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
            [SNew(SButton).Text(LOCTEXT("ImportCustomDonor", "Initialize brush")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::ImportCustomDonor)]
            + SHorizontalBox::Slot().AutoWidth().Padding(0, 0, 4, 0)
            [SNew(SButton).Text(LOCTEXT("OpenCustomDonor", "Open donor editor")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::OpenCustomDonor)]
            + SHorizontalBox::Slot().AutoWidth()
            [SNew(SButton).Text(LOCTEXT("CookCustomDonor", "Cook and certify")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::CookCustomDonor)]
        ];
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(CustomActorNameText, SEditableTextBox).Text(FText::FromString(TEXT("UT4Recon_CustomCollision"))).HintText(LOCTEXT("CustomActorNameHint", "New collision actor name"))];
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(CustomLocationText, SEditableTextBox).Text(FText::FromString(TEXT("{\"x\":0,\"y\":0,\"z\":0}"))).HintText(LOCTEXT("CustomLocationHint", "Location JSON"))];
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(CustomRotationText, SEditableTextBox).Text(FText::FromString(TEXT("{\"pitch\":0,\"yaw\":0,\"roll\":0}"))).HintText(LOCTEXT("CustomRotationHint", "Rotation JSON"))];
        Box->AddSlot().AutoHeight().Padding(0, 2)
        [SAssignNew(CustomScaleText, SEditableTextBox).Text(FText::FromString(TEXT("{\"x\":1,\"y\":1,\"z\":1}"))).HintText(LOCTEXT("CustomScaleHint", "Scale JSON"))];
        Box->AddSlot().AutoHeight().Padding(0, 4)
        [SNew(SButton).Text(LOCTEXT("AddCustomDonor", "Add certified collision to edit manifest")).IsEnabled(this, &SUt4ReconPanel::CanStartAction).OnClicked(this, &SUt4ReconPanel::AddCustomDonor)];
        return SNew(SScrollBox) + SScrollBox::Slot()[Box];
    }

    bool LoadJsonObject(const FString& Path, TSharedPtr<FJsonObject>& Result) const
    {
        FString Json;
        if (!FFileHelper::LoadFileToString(Json, *Path)) return false;
        const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
        return FJsonSerializer::Deserialize(Reader, Result) && Result.IsValid();
    }

    void AddPendingOperations(const TSharedRef<SVerticalBox>& Box, const FString& ManifestPath)
    {
        TSharedPtr<FJsonObject> Manifest; if (!LoadJsonObject(ManifestPath, Manifest)) return;
        const TArray<TSharedPtr<FJsonValue>>* Operations = nullptr;
        if (!Manifest->TryGetArrayField(TEXT("operations"), Operations) || !Operations) return;
        AddHeading(Box, TEXT("Validated pending operations"));
        AddLine(Box, FString::Printf(TEXT("%d operation(s) currently recorded in the edit manifest."), Operations->Num()));
        const int32 Visible = FMath::Min(Operations->Num(), 12);
        for (int32 Index = 0; Index < Visible; ++Index)
        {
            const TSharedPtr<FJsonObject> Operation = (*Operations)[Index]->AsObject();
            if (!Operation.IsValid()) continue;
            FString Description = JsonString(Operation, TEXT("operation"));
            const FString Property = JsonString(Operation, TEXT("propertyPath"), TEXT("")); if (!Property.IsEmpty()) Description += TEXT(": ") + Property;
            const FString Target = JsonString(Operation, TEXT("objectPath"), TEXT("")); if (!Target.IsEmpty()) Description += TEXT(" -> ") + Target;
            AddLine(Box, TEXT("+ ") + Description);
        }
        if (Operations->Num() > Visible) AddLine(Box, FString::Printf(TEXT("...and %d more. Open reports for the complete manifest."), Operations->Num() - Visible));
    }

    void AddReportSummary(const TSharedRef<SVerticalBox>& Box)
    {
        const FString Reports = FPaths::Combine(RecoveryProject, TEXT(".ut4recon/reports"));
        TSharedPtr<FJsonObject> Report;
        if (LoadJsonObject(FPaths::Combine(WorkspaceRoot, TEXT("editor-map-check.json")), Report))
            AddLine(Box, FString::Printf(TEXT("Map Check: %.0f error(s), %.0f warning(s)"), Report->GetNumberField(TEXT("errors")), Report->GetNumberField(TEXT("warnings"))));
        if (LoadJsonObject(FPaths::Combine(Reports, TEXT("editor-diff.json")), Report))
            AddLine(Box, FString::Printf(TEXT("Editor diff: %.0f changed properties; %s"), Report->GetNumberField(TEXT("changedProperties")), Report->GetBoolField(TEXT("passed")) ? TEXT("passed") : TEXT("failed")));
        if (LoadJsonObject(FPaths::Combine(Reports, TEXT("behavior-validation.json")), Report))
            AddLine(Box, FString::Printf(TEXT("Compiled behavior: %s"), Report->GetBoolField(TEXT("passed")) ? TEXT("preserved") : TEXT("failed")));
        if (LoadJsonObject(FPaths::Combine(Reports, TEXT("build-validation.json")), Report))
        {
            const TSharedPtr<FJsonObject>* Output = nullptr;
            AddLine(Box, FString::Printf(TEXT("Last pak build: %s"), Report->GetBoolField(TEXT("passed")) ? TEXT("passed") : TEXT("failed")));
            if (Report->TryGetObjectField(TEXT("outputPak"), Output) && Output && Output->IsValid()) AddLine(Box, JsonString(*Output, TEXT("path")));
        }
        if (LoadJsonObject(FPaths::Combine(Reports, TEXT("runtime-certification.json")), Report))
        {
            const bool bPassed = Report->HasTypedField<EJson::Boolean>(TEXT("passed")) && Report->GetBoolField(TEXT("passed"));
            const FString Evidence = JsonString(Report, TEXT("evidenceLevel"), TEXT("unknown"));
            AddLine(Box, FString::Printf(TEXT("Runtime %s: %s"), *Evidence, bPassed ? TEXT("passed") : TEXT("failed")), bPassed ? FLinearColor(0.35f, 0.9f, 0.45f) : FLinearColor(1.0f, 0.35f, 0.35f));
            AddLine(Box, TEXT("Live actor state and player interaction: not certified"), FLinearColor(1.0f, 0.72f, 0.2f));
        }
    }

    bool CanStartAction() const { return !BackendProcess.IsValid() && LoadError.IsEmpty(); }
    bool CanValidateChanges() const
    {
        const FString MapFile = EditorMapFile();
        return CanStartAction() && !MapFile.IsEmpty() && FPaths::FileExists(MapFile) && FPaths::FileExists(WorkspaceExportPath) &&
            IFileManager::Get().GetTimeStamp(*WorkspaceExportPath) >= IFileManager::Get().GetTimeStamp(*MapFile);
    }
    bool CanBuildPak() const { FString Reason; return CanStartAction() && IsBuildInputCurrent(Reason); }
    FString EditorMapFile() const
    {
        if (ProjectFile.IsEmpty() || !MapPackagePath.StartsWith(TEXT("/Game/"))) return TEXT("");
        FString Relative = MapPackagePath.Mid(6); Relative.ReplaceInline(TEXT("/"), TEXT("\\"));
        return FPaths::Combine(FPaths::GetPath(ProjectFile), TEXT("Content"), Relative + TEXT(".umap"));
    }
    bool IsBuildInputCurrent(FString& Reason) const
    {
        const FString MapFile = EditorMapFile();
        const FString DiffReport = FPaths::Combine(RecoveryProject, TEXT(".ut4recon/reports/editor-diff.json"));
        const FString EditManifest = FPaths::Combine(RecoveryProject, TEXT(".ut4recon/edit-manifest.json"));
        if (MapFile.IsEmpty() || !FPaths::FileExists(MapFile)) { Reason = TEXT("workspace map is missing; recreate or reimport the workspace"); return false; }
        if (!FPaths::FileExists(WorkspaceExportPath)) { Reason = TEXT("export the saved map"); return false; }
        if (IFileManager::Get().GetTimeStamp(*WorkspaceExportPath) < IFileManager::Get().GetTimeStamp(*MapFile)) { Reason = TEXT("saved map is newer than the export; export it again"); return false; }
        if (!FPaths::FileExists(DiffReport) || !FPaths::FileExists(EditManifest)) { Reason = TEXT("validate the current export"); return false; }
        if (IFileManager::Get().GetTimeStamp(*DiffReport) < IFileManager::Get().GetTimeStamp(*WorkspaceExportPath) || IFileManager::Get().GetTimeStamp(*EditManifest) < IFileManager::Get().GetTimeStamp(*WorkspaceExportPath)) { Reason = TEXT("export is newer than validation; validate it again"); return false; }
        TSharedPtr<FJsonObject> Report;
        if (!LoadJsonObject(DiffReport, Report) || !Report->HasTypedField<EJson::Boolean>(TEXT("passed")) || !Report->GetBoolField(TEXT("passed"))) { Reason = TEXT("validation failed; open the report, correct the listed edits, and validate again"); return false; }
        Reason = TEXT("current editor export and validation are ready"); return true;
    }
    FReply ExportSavedMap() { return StartBackendAction(TEXT("Export saved map"), { TEXT("run-editor-export"), WorkspaceRoot }); }
    FReply ValidateChanges() { return StartBackendAction(TEXT("Validate editor changes"), { TEXT("export-editor-edits"), RecoveryProject, WorkspaceRoot }); }
    FReply BuildPak()
    {
        FString Readiness;
        if (!IsBuildInputCurrent(Readiness))
        {
            ActionStatus = TEXT("Build was not started: ") + Readiness + TEXT("."); bActionFailed = true; Rebuild(); return FReply::Handled();
        }
        const FString Output = OutputPakText.IsValid() ? OutputPakText->GetText().ToString() : DefaultOutputPak;
        const FString MapName = MapNameText.IsValid() ? MapNameText->GetText().ToString() : DefaultMapName;
        if (Output.IsEmpty() || MapName.IsEmpty())
        {
            ActionStatus = TEXT("Build was not started: output pak and distinct map name are required."); bActionFailed = true; Rebuild(); return FReply::Handled();
        }
        SelectedOutputPak = Output; SelectedMapName = MapName; SaveBuildSettings();
        PendingActionName = TEXT("Build repaired pak");
        PendingArguments = { TEXT("build"), RecoveryProject, TEXT("--output"), Output, TEXT("--rename-map"), MapName };
        return StartBackendAction(TEXT("Prepare distinct map title"), { TEXT("set-map-title"), RecoveryProject, TEXT("--title"), MapName });
    }
    FReply OpenReports()
    {
        const FString Reports = FPaths::Combine(RecoveryProject, TEXT(".ut4recon/reports"));
        IFileManager::Get().MakeDirectory(*Reports, true); FPlatformProcess::ExploreFolder(*Reports); return FReply::Handled();
    }
    FReply OpenActionLog()
    {
        if (!BackendLogPath.IsEmpty() && FPaths::FileExists(BackendLogPath)) FPlatformProcess::LaunchFileInDefaultExternalApplication(*BackendLogPath);
        return FReply::Handled();
    }
    void CountMeshPreviewState(int32& Available, int32& Missing) const
    {
        Available = 0; Missing = 0; TSharedPtr<FJsonObject> Manifest;
        if (!LoadJsonObject(MeshPreviewImportPath, Manifest)) return;
        const TArray<TSharedPtr<FJsonValue>>* Entries = nullptr;
        if (!Manifest->TryGetArrayField(TEXT("entries"), Entries) || !Entries) return;
        for (const TSharedPtr<FJsonValue>& Value : *Entries)
        {
            const TSharedPtr<FJsonObject> Entry = Value->AsObject(); if (!Entry.IsValid()) continue;
            const FString PackagePath = JsonString(Entry, TEXT("packagePath"), TEXT("")); FString Filename;
            if (!PackagePath.IsEmpty() && FPackageName::DoesPackageExist(PackagePath, nullptr, &Filename)) ++Available; else ++Missing;
        }
    }
    FReply ImportMeshPreviews()
    {
        TSharedPtr<FJsonObject> Manifest;
        if (!LoadJsonObject(MeshPreviewImportPath, Manifest))
        {
            PreviewImportStatus = TEXT("Preview import failed: the manifest could not be read."); bPreviewImportFailed = true; Rebuild(); return FReply::Handled();
        }
        const TArray<TSharedPtr<FJsonValue>>* Entries = nullptr;
        if (!Manifest->TryGetArrayField(TEXT("entries"), Entries) || !Entries)
        {
            PreviewImportStatus = TEXT("Preview import failed: the manifest contains no entries."); bPreviewImportFailed = true; Rebuild(); return FReply::Handled();
        }
        TMap<FString, TArray<FString>> FilesByDestination; int32 Existing = 0, Requested = 0;
        for (const TSharedPtr<FJsonValue>& Value : *Entries)
        {
            const TSharedPtr<FJsonObject> Entry = Value->AsObject(); if (!Entry.IsValid()) continue;
            const FString PackagePath = JsonString(Entry, TEXT("packagePath"), TEXT(""));
            const FString SourceObj = JsonString(Entry, TEXT("sourceObj"), TEXT(""));
            const FString DestinationPath = JsonString(Entry, TEXT("destinationPath"), TEXT(""));
            FString ExistingFilename;
            if (!PackagePath.IsEmpty() && FPackageName::DoesPackageExist(PackagePath, nullptr, &ExistingFilename)) { ++Existing; continue; }
            if (!SourceObj.IsEmpty() && !DestinationPath.IsEmpty() && FPaths::FileExists(SourceObj))
            {
                FilesByDestination.FindOrAdd(DestinationPath).Add(SourceObj); ++Requested;
            }
        }
        if (Requested == 0)
        {
            PreviewImportStatus = FString::Printf(TEXT("No preview import was needed; %d source package(s) already exist."), Existing);
            bPreviewImportFailed = false;
            UE_LOG(LogUt4ReconEditor, Log, TEXT("%s"), *PreviewImportStatus);
            Rebuild();
            if (FParse::Param(FCommandLine::Get(), TEXT("UT4ReconPreviewImportProbe"))) FPlatformMisc::RequestExit(false);
            return FReply::Handled();
        }
        TArray<UObject*> Imported; bPreviewImportFailed = false;
        for (const TPair<FString, TArray<FString>>& Group : FilesByDestination)
        {
            UClass* ImportDataClass = LoadClass<UObject>(nullptr, TEXT("/Script/UnrealEd.AutomatedAssetImportData"));
            if (!ImportDataClass) { bPreviewImportFailed = true; continue; }
            UAutomatedAssetImportData* Data = static_cast<UAutomatedAssetImportData*>(NewObject<UObject>(GetTransientPackage(), ImportDataClass));
            Data->GroupName = TEXT("UT4 Recon recovered mesh previews"); Data->Filenames = Group.Value; Data->DestinationPath = Group.Key;
            Data->FactoryName = TEXT("FbxFactory"); Data->bReplaceExisting = false; Data->bSkipReadOnly = true; Data->Factory = nullptr; Data->Initialize();
            if (!Data->IsValid()) { bPreviewImportFailed = true; continue; }
            Imported.Append(FAssetToolsModule::GetModule().Get().ImportAssetsAutomated(*Data));
        }
        bool Saved = true;
        for (UObject* Object : Imported)
        {
            if (!Object) { Saved = false; continue; }
            UPackage* Package = Object->GetOutermost();
            const FString Filename = FPackageName::LongPackageNameToFilename(Package->GetName(), FPackageName::GetAssetPackageExtension());
            Saved = UPackage::SavePackage(Package, Object, RF_Public | RF_Standalone, *Filename, GError, nullptr, false, true, SAVE_NoError) && Saved;
        }
        bPreviewImportFailed = bPreviewImportFailed || Imported.Num() != Requested || !Saved;
        PreviewImportStatus = FString::Printf(TEXT("Imported %d of %d recovered mesh preview(s); skipped %d existing package(s). Content save: %s."), Imported.Num(), Requested, Existing, Saved ? TEXT("passed") : TEXT("failed"));
        UE_LOG(LogUt4ReconEditor, Log, TEXT("%s"), *PreviewImportStatus); Rebuild();
        if (FParse::Param(FCommandLine::Get(), TEXT("UT4ReconPreviewImportProbe"))) FPlatformMisc::RequestExit(false);
        return FReply::Handled();
    }
    FString CustomWorkspace() const { return CustomWorkspaceText.IsValid() ? CustomWorkspaceText->GetText().ToString() : FPaths::Combine(RecoveryProject, TEXT("CustomCollisionDonor")); }
    FReply CreateCustomDonor()
    {
        const FString Path = CustomWorkspace();
        return StartBackendAction(TEXT("Create custom collision workspace"), { TEXT("create-custom-collision-workspace"), TEXT("--output"), Path, TEXT("--editor"), ActiveEditorRoot });
    }
    FReply ImportCustomDonor() { return StartBackendAction(TEXT("Initialize custom collision brush"), { TEXT("run-custom-collision-import"), CustomWorkspace() }); }
    FReply OpenCustomDonor() { return StartBackendAction(TEXT("Open custom collision editor"), { TEXT("open-custom-collision-workspace"), CustomWorkspace() }); }
    FReply CookCustomDonor() { return StartBackendAction(TEXT("Cook and certify custom collision"), { TEXT("cook-custom-collision-donor"), CustomWorkspace() }); }
    FReply AddCustomDonor()
    {
        const FString Path = CustomWorkspace(); const FString Manifest = FPaths::Combine(Path, TEXT("custom-collision-donor.json"));
        return StartBackendAction(TEXT("Add certified custom collision"), { TEXT("add-custom-collision"), RecoveryProject, TEXT("--donor"), Manifest,
            TEXT("--name"), CustomActorNameText->GetText().ToString(), TEXT("--location"), CustomLocationText->GetText().ToString(),
            TEXT("--rotation"), CustomRotationText->GetText().ToString(), TEXT("--scale"), CustomScaleText->GetText().ToString(), TEXT("--append") });
    }

    FString QuoteArgument(const FString& Value) const
    {
        FString Escaped = Value.Replace(TEXT("\""), TEXT("\\\""));
        return TEXT("\"") + Escaped + TEXT("\"");
    }

    FReply StartBackendAction(const FString& Name, const TArray<FString>& Arguments)
    {
        if (!CanStartAction()) return FReply::Handled();
        TArray<FString> CompleteArguments = BackendArguments; CompleteArguments.Append(Arguments);
        FString Parameters;
        for (const FString& Argument : CompleteArguments) Parameters += (Parameters.IsEmpty() ? TEXT("") : TEXT(" ")) + QuoteArgument(Argument);
        BackendLogPath = FPaths::Combine(WorkspaceRoot, TEXT("backend-action.log")); FFileHelper::SaveStringToFile(TEXT(""), *BackendLogPath);
        FPlatformProcess::CreatePipe(BackendReadPipe, BackendWritePipe);
        uint32 ProcessId = 0;
        BackendProcess = FPlatformProcess::CreateProc(*BackendExecutable, *Parameters, false, true, true, &ProcessId, 0, *WorkspaceRoot, BackendWritePipe, nullptr);
        if (!BackendProcess.IsValid())
        {
            CloseBackendPipes(); ActionStatus = FString::Printf(TEXT("%s failed to start."), *Name); bActionFailed = true;
        }
        else
        {
            RunningAction = Name; ActionStartedAt = FPlatformTime::Seconds(); ActionStatus = FString::Printf(TEXT("%s is running in process %u..."), *Name, ProcessId); bActionFailed = false;
            UE_LOG(LogUt4ReconEditor, Log, TEXT("Started backend action '%s': %s %s"), *Name, *BackendExecutable, *Parameters);
        }
        Rebuild(); return FReply::Handled();
    }

    void PollBackendProcess()
    {
        if (!BackendProcess.IsValid()) return;
        AppendBackendOutput();
        if (FPlatformProcess::IsProcRunning(BackendProcess)) return;
        int32 ReturnCode = -1; FPlatformProcess::GetProcReturnCode(BackendProcess, &ReturnCode); AppendBackendOutput();
        FPlatformProcess::CloseProc(BackendProcess); BackendProcess.Reset(); CloseBackendPipes();
        bActionFailed = ReturnCode != 0 && !(RunningAction == TEXT("Export saved map") && ReturnCode == 2);
        ActionStatus = FString::Printf(TEXT("%s %s (exit code %d)."), *RunningAction,
            bActionFailed ? TEXT("failed") : ReturnCode == 2 ? TEXT("completed with Map Check warnings") : TEXT("completed"), ReturnCode);
        if (bActionFailed)
        {
            if (RunningAction == TEXT("Export saved map")) ActionStatus += TEXT(" Save the map, close any other editor process using this workspace, open the action log, and retry Export.");
            else if (RunningAction == TEXT("Validate editor changes")) ActionStatus += TEXT(" Open the action log and reports, correct the listed unsupported or ambiguous edits, then retry Validate.");
            else if (RunningAction == TEXT("Prepare distinct map title")) ActionStatus += TEXT(" Use a non-empty title different from the cooked baseline, then retry Build.");
            else if (RunningAction == TEXT("Build repaired pak")) ActionStatus += TEXT(" Open the action log and reports; the baseline and editor workspace remain unchanged and the build can be retried.");
            else ActionStatus += TEXT(" Open the action log for the failing command and retry after correcting it.");
        }
        UE_LOG(LogUt4ReconEditor, Log, TEXT("%s"), *ActionStatus);
        FString BuildReadiness; const bool bReadyToBuild = IsBuildInputCurrent(BuildReadiness);
        UE_LOG(LogUt4ReconEditor, Log, TEXT("Build readiness after action: %s (%s)."), bReadyToBuild ? TEXT("ready") : TEXT("blocked"), *BuildReadiness);
        RunningAction.Empty();
        if (!bActionFailed && !PendingActionName.IsEmpty())
        {
            const FString NextName = PendingActionName; const TArray<FString> NextArguments = PendingArguments;
            PendingActionName.Empty(); PendingArguments.Empty(); StartBackendAction(NextName, NextArguments); return;
        }
        PendingActionName.Empty(); PendingArguments.Empty();
        UpdateActivityStrip();
        if (bActionProbe)
        {
            UE_LOG(LogUt4ReconEditor, Log, TEXT("Backend-action certification probe %s."), bActionFailed ? TEXT("failed") : TEXT("passed"));
            FPlatformMisc::RequestExit(false);
        }
        if (CurrentView == EView::Build) Rebuild();
    }

    void AppendBackendOutput()
    {
        if (!BackendReadPipe) return; const FString Output = FPlatformProcess::ReadPipe(BackendReadPipe);
        if (!Output.IsEmpty()) FFileHelper::SaveStringToFile(Output, *BackendLogPath, FFileHelper::EEncodingOptions::AutoDetect, &IFileManager::Get(), FILEWRITE_Append);
    }

    void CloseBackendPipes()
    {
        if (BackendReadPipe || BackendWritePipe) FPlatformProcess::ClosePipe(BackendReadPipe, BackendWritePipe);
        BackendReadPipe = nullptr; BackendWritePipe = nullptr;
    }

private:
    using FGetSelectedActors = void* (*)(void*);
    using FGetTop = void* (*)(void*, void*, void*, bool);
    using FGetActorLabel = const FString* (*)(void*);
    using FGetStaticClass = void* (*)();
    using FGetObjectsOfClass = void (*)(void*, TArray<void*>&, bool, uint32, int32);
    using FSetActorHidden = void (*)(void*, bool);
    using FIsActorHidden = bool (*)(void*, bool);
    using FRedrawAllViewports = void (*)(void*, bool);
    using FSelectActor = void (*)(void*, void*, bool, bool, bool, bool);

    EView CurrentView = EView::Map;
    FString WorkspaceRoot, ActiveEditorRoot, WorkspaceManifestPath, BuildSettingsPath, ProxyScenePath, SupportReportPath, VisualizationIndexPath, MeshPreviewImportPath, RecoveryProject, BackendExecutable, DefaultOutputPak, DefaultMapName, SelectedOutputPak, SelectedMapName, WorkspaceMode;
    FString ProjectFile, MapPackagePath, WorkspaceExportPath;
    FString LoadError, SelectionApiError, LastSelectedActorLabel, RunningAction, ActionStatus, BackendLogPath, ActionProbeMode, PendingActionName, PreviewImportStatus;
    TArray<FString> BackendArguments;
    TArray<FString> PendingArguments;
    TSharedPtr<FJsonObject> Scene;
    TSharedPtr<FJsonObject> Visualization;
    TMap<FString, TSharedPtr<FJsonObject>> ObjectsById;
    TMap<FString, TSharedPtr<FJsonObject>> ObjectsByEditorName;
    TMap<FString, TSharedPtr<FJsonObject>> ObjectsByPath;
    TMap<FString, bool> FilterVisibility;
    TSharedPtr<SBorder> ContentBorder, ActivityBorder;
    TSharedPtr<SEditableTextBox> OutputPakText, MapNameText;
    TSharedPtr<SEditableTextBox> CustomWorkspaceText, CustomActorNameText, CustomLocationText, CustomRotationText, CustomScaleText;
    FProcHandle BackendProcess;
    void* BackendReadPipe = nullptr;
    void* BackendWritePipe = nullptr;
    bool bActionFailed = false;
    bool bShowSelectionDetails = false;
    bool bShowBuildDetails = false;
    bool bPreviewImportFailed = false;
    bool bActionProbe = false;
    bool bPreviewImportProbePending = false;
    void* UnrealEdHandle = nullptr;
    void* EngineHandle = nullptr;
    void* CoreUObjectHandle = nullptr;
    void** GEditorAddress = nullptr;
    FGetSelectedActors GetSelectedActors = nullptr;
    FGetTop GetTop = nullptr;
    FGetActorLabel GetActorLabel = nullptr;
    FGetStaticClass GetUObjectStaticClass = nullptr;
    FGetStaticClass GetActorStaticClass = nullptr;
    FGetObjectsOfClass GetObjectsOfClass = nullptr;
    FSetActorHidden SetActorHidden = nullptr;
    FIsActorHidden IsActorHidden = nullptr;
    FRedrawAllViewports RedrawAllViewports = nullptr;
    FSelectActor SelectActor = nullptr;
    void* ProbeActor = nullptr;
    bool bFilterProbePending = false;
    double LastFilterProbeAttemptTime = -5.0;
    double LastPreviewProbeAttemptTime = -15.0;
    double ActionStartedAt = 0.0;
};

class FUt4ReconEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override
    {
        FString PreloadObjectPath;
        if (FParse::Value(FCommandLine::Get(), TEXT("UT4ReconPreload="), PreloadObjectPath) && !PreloadObjectPath.IsEmpty())
        {
            UObject* PreloadedObject = StaticLoadObject(UObject::StaticClass(), nullptr, *PreloadObjectPath);
            UE_LOG(LogUt4ReconEditor, Log, TEXT("UT4 Recon preload '%s': %s."), *PreloadObjectPath,
                PreloadedObject ? TEXT("loaded") : TEXT("failed"));
        }
        FGlobalTabmanager::Get()->RegisterNomadTabSpawner(Ut4ReconTabName, FOnSpawnTab::CreateRaw(this, &FUt4ReconEditorModule::SpawnTab))
            .SetDisplayName(LOCTEXT("TabTitle", "UT4 Recon"))
            .SetTooltipText(LOCTEXT("TabTooltip", "Inspect the active cooked-map recovery workspace."))
            .SetGroup(WorkspaceMenu::GetMenuStructure().GetToolsCategory());
        UE_LOG(LogUt4ReconEditor, Log, TEXT("UT4 Recon editor adapter loaded."));
        if (FParse::Param(FCommandLine::Get(), TEXT("UT4ReconOpen"))) FGlobalTabmanager::Get()->InvokeTab(Ut4ReconTabName);
    }

    virtual void ShutdownModule() override
    {
        FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(Ut4ReconTabName);
        UE_LOG(LogUt4ReconEditor, Log, TEXT("UT4 Recon editor adapter unloaded."));
    }

private:
    TSharedRef<SDockTab> SpawnTab(const FSpawnTabArgs&)
    {
        return SNew(SDockTab).TabRole(ETabRole::NomadTab)[SNew(SUt4ReconPanel)];
    }
};

IMPLEMENT_MODULE(FUt4ReconEditorModule, Ut4ReconEditor)

#undef LOCTEXT_NAMESPACE
