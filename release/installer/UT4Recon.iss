#ifndef PayloadRoot
  #error PayloadRoot must point to a complete UT4 Recon portable release directory.
#endif
#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1.
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Installer.ps1.
#endif

#define ProductName "UT4 Recon"
#define RequiredCompatibleChangelist "3525109"

[Setup]
AppId={{2A16919B-B7C6-4B68-9374-091CC05C1CB2}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=UT4 Recon contributors
AppPublisherURL=https://github.com/jjherrmann98/UT4-Recon
AppSupportURL=https://github.com/jjherrmann98/UT4-Recon/issues
AppUpdatesURL=https://github.com/jjherrmann98/UT4-Recon/releases
DefaultDirName={autopf}\UT4 Recon
DefaultGroupName=UT4 Recon
DisableProgramGroupPage=yes
LicenseFile={#PayloadRoot}\LICENSE
InfoBeforeFile={#PayloadRoot}\PROTOTYPE-README.md
OutputDir={#OutputDir}
OutputBaseFilename=UT4Recon-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\cli\Ut4Recon.Cli.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadRoot}\EditorPlugin\Ut4ReconEditor\*"; DestDir: "{code:GetEditorPluginDir}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourcePath}\Input-Paks-README.txt"; DestDir: "{code:GetInputPakDir}"; DestName: "README.txt"; Flags: onlyifdoesntexist uninsneveruninstall

[Dirs]
Name: "{code:GetInputPakDir}"; Flags: uninsneveruninstall
Name: "{code:GetRecoveryProjectDir}"; Flags: uninsneveruninstall
Name: "{code:GetBuiltPakDir}"; Flags: uninsneveruninstall

[Icons]
Name: "{group}\UT4 Recon documentation"; Filename: "{app}\README.md"
Name: "{group}\UT4 Recon command prompt"; Filename: "{cmd}"; Parameters: "/K cd /d ""{app}"""; WorkingDir: "{app}"
Name: "{group}\Input Paks"; Filename: "{code:GetInputPakDir}"
Name: "{group}\Recovery Projects"; Filename: "{code:GetRecoveryProjectDir}"
Name: "{group}\Built Paks"; Filename: "{code:GetBuiltPakDir}"
Name: "{group}\Launch UT4 Editor"; Filename: "{code:GetEditorExecutable}"; WorkingDir: "{code:GetEditorRoot}"
Name: "{group}\Uninstall UT4 Recon"; Filename: "{uninstallexe}"

[Registry]
Root: HKA; Subkey: "Software\UT4Recon"; ValueType: string; ValueName: "EditorRoot"; ValueData: "{code:GetEditorRoot}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\UT4Recon"; ValueType: string; ValueName: "DataRoot"; ValueData: "{code:GetDataRoot}"; Flags: uninsdeletekey

[Code]
var
  EditorPage: TInputDirWizardPage;
  DataPage: TInputDirWizardPage;

function NormalizeRoot(Path: String): String;
begin
  Result := RemoveBackslashUnlessRoot(ExpandFileName(Path));
end;

function GetEditorRoot(Param: String): String;
begin
  Result := NormalizeRoot(EditorPage.Values[0]);
end;

function GetEditorPluginDir(Param: String): String;
begin
  Result := AddBackslash(GetEditorRoot('')) +
    'Engine\Plugins\Marketplace\Ut4ReconEditor';
end;

function GetDataRoot(Param: String): String;
begin
  Result := NormalizeRoot(DataPage.Values[0]);
end;

function GetInputPakDir(Param: String): String;
begin
  Result := AddBackslash(GetDataRoot('')) + 'Input Paks';
end;

function GetRecoveryProjectDir(Param: String): String;
begin
  Result := AddBackslash(GetDataRoot('')) + 'Recovery Projects';
end;

function GetBuiltPakDir(Param: String): String;
begin
  Result := AddBackslash(GetDataRoot('')) + 'Built Paks';
end;

function GetEditorExecutable(Param: String): String;
begin
  Result := AddBackslash(GetEditorRoot('')) +
    'Engine\Binaries\Win64\UE4Editor.exe';
end;

function CompactJson(Value: String): String;
begin
  StringChangeEx(Value, ' ', '', True);
  StringChangeEx(Value, #9, '', True);
  StringChangeEx(Value, #13, '', True);
  StringChangeEx(Value, #10, '', True);
  Result := Value;
end;

function ValidateEditorRoot(Root: String; var Reason: String): Boolean;
var
  ModulesPath: String;
  ModulesJson: AnsiString;
  Compact: String;
begin
  Result := False;
  Root := NormalizeRoot(Root);
  ModulesPath := AddBackslash(Root) +
    'Engine\Binaries\Win64\UE4Editor.modules';

  if not FileExists(ModulesPath) then
  begin
    Reason := 'UT4 Editor was not found in:' + #13#10 + Root + #13#10#13#10 +
      'Select the directory that contains Engine\Binaries\Win64\UE4Editor.modules.';
    Exit;
  end;

  if not LoadStringFromFile(ModulesPath, ModulesJson) then
  begin
    Reason := 'The installer could not read:' + #13#10 + ModulesPath;
    Exit;
  end;

  Compact := CompactJson(String(ModulesJson));
  if Pos('"CompatibleChangelist":{#RequiredCompatibleChangelist}', Compact) = 0 then
  begin
    Reason := 'This release requires UT4 Editor API {#RequiredCompatibleChangelist}.' + #13#10#13#10 +
      'The selected editor does not report that compatible changelist.';
    Exit;
  end;

  Result := True;
end;

function EditorIsRunning: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq UE4Editor.exe" /NH | find /I "UE4Editor.exe" >NUL',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitialEditorRoot: String;
var
  Candidate: String;
  Reason: String;
  DriveCode: Integer;
begin
  Candidate := ExpandConstant('{param:EDITORROOT|}');
  if Candidate <> '' then
  begin
    Result := Candidate;
    Exit;
  end;

  if RegQueryStringValue(HKA, 'Software\UT4Recon', 'EditorRoot', Candidate) and
    ValidateEditorRoot(Candidate, Reason) then
  begin
    Result := Candidate;
    Exit;
  end;

  Candidate := ExpandConstant('{autopf}\Epic Games\UnrealTournamentEditor');
  if ValidateEditorRoot(Candidate, Reason) then
  begin
    Result := Candidate;
    Exit;
  end;

  for DriveCode := Ord('C') to Ord('Z') do
  begin
    Candidate := Chr(DriveCode) + ':\UT4\UnrealTournamentEditor';
    if ValidateEditorRoot(Candidate, Reason) then
    begin
      Result := Candidate;
      Exit;
    end;
    Candidate := Chr(DriveCode) + ':\UnrealTournamentEditor';
    if ValidateEditorRoot(Candidate, Reason) then
    begin
      Result := Candidate;
      Exit;
    end;
    Candidate := Chr(DriveCode) + ':\Games\UnrealTournamentEditor';
    if ValidateEditorRoot(Candidate, Reason) then
    begin
      Result := Candidate;
      Exit;
    end;
  end;

  Result := 'C:\UnrealTournamentEditor';
end;

function InitialDataRoot: String;
var
  Candidate: String;
begin
  Candidate := ExpandConstant('{param:DATAROOT|}');
  if Candidate = '' then
    RegQueryStringValue(HKA, 'Software\UT4Recon', 'DataRoot', Candidate);
  if Candidate = '' then Candidate := ExpandConstant('{commondocs}\UT4 Recon');
  Result := Candidate;
end;

procedure InitializeWizard;
begin
  EditorPage := CreateInputDirPage(wpSelectDir,
    'Locate UT4 Editor',
    'Select the compatible Unreal Tournament 4 Editor installation.',
    'UT4 Recon will verify API changelist {#RequiredCompatibleChangelist} and install its editor plugin under Engine\Plugins\Marketplace. Close the editor before continuing.',
    False, 'New Folder');
  EditorPage.Add('UT4 Editor root:');
  EditorPage.Values[0] := InitialEditorRoot();

  DataPage := CreateInputDirPage(EditorPage.ID,
    'Choose the UT4 Recon working folder',
    'Select where maps and recovery projects should be stored.',
    'Setup creates Input Paks, Recovery Projects, and Built Paks here. These user files are preserved when UT4 Recon is uninstalled.',
    False, 'New Folder');
  DataPage.Add('Working folder:');
  DataPage.Values[0] := InitialDataRoot();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Reason: String;
begin
  Result := True;
  if CurPageID = EditorPage.ID then
  begin
    if not ValidateEditorRoot(EditorPage.Values[0], Reason) then
    begin
      MsgBox(Reason, mbError, MB_OK);
      Result := False;
      Exit;
    end;
    EditorPage.Values[0] := NormalizeRoot(EditorPage.Values[0]);
  end;
  if CurPageID = DataPage.ID then
  begin
    if Trim(DataPage.Values[0]) = '' then
    begin
      MsgBox('Choose a working folder for input paks and recovery projects.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    DataPage.Values[0] := NormalizeRoot(DataPage.Values[0]);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Reason: String;
begin
  Result := '';
  if not ValidateEditorRoot(EditorPage.Values[0], Reason) then
  begin
    Result := Reason;
    Exit;
  end;

  if EditorIsRunning() then
    Result := 'Close UT4 Editor (UE4Editor.exe) before installing or updating UT4 Recon.';
end;
