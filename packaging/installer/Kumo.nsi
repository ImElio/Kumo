; Per-user installer for Kumo. Built by tools/package-release.ps1; see docs/distribution.md.
;
; Required defines (makensis /D...):
;   VERSION          SemVer version, e.g. 0.2.0-beta.1 (Kumo's updater checks it against the release)
;   VERSION_NUMERIC  Major.Minor.Patch.0 for the Windows version resource
;   ARCH             x64 or arm64
;   SOURCE_DIR       dotnet publish output for that architecture
;   OUTFILE          Kumo-<version>-win-<arch>-setup.exe
;   ICON, LICENSE_FILE
; Optional:
;   SIGN_COMMAND     Command that signs "%1"; applied to the installer and the embedded uninstaller.

!macro RequireDefine NAME
  !ifndef ${NAME}
    !error "Missing /D${NAME}; build with tools/package-release.ps1."
  !endif
!macroend
!insertmacro RequireDefine VERSION
!insertmacro RequireDefine VERSION_NUMERIC
!insertmacro RequireDefine ARCH
!insertmacro RequireDefine SOURCE_DIR
!insertmacro RequireDefine OUTFILE
!insertmacro RequireDefine ICON
!insertmacro RequireDefine LICENSE_FILE

Unicode true
ManifestDPIAware true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Kumo"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUNTIME_URL "https://dotnet.microsoft.com/download/dotnet/10.0"

!ifdef SIGN_COMMAND
  !finalize '${SIGN_COMMAND}'
  !uninstfinalize '${SIGN_COMMAND}'
!endif

Name "Kumo"
AllowSkipFiles off
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\Kumo"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
BrandingText "Kumo ${VERSION}"
ShowInstDetails nevershow
ShowUninstDetails nevershow

VIProductVersion "${VERSION_NUMERIC}"
VIFileVersion "${VERSION_NUMERIC}"
VIAddVersionKey "ProductName" "Kumo"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION_NUMERIC}"
VIAddVersionKey "FileDescription" "Kumo ${VERSION} installer"
VIAddVersionKey "CompanyName" "ImElio"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 ImElio"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "${__FILEDIR__}\sidebar.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${__FILEDIR__}\sidebar.bmp"
!define MUI_WELCOMEPAGE_TITLE "Welcome to Kumo"
!define MUI_WELCOMEPAGE_TEXT "Kumo lives in the notification area. Drag files in File Explorer, tap Shift, and pick what to do with them.$\r$\n$\r$\nKumo installs for your account only and doesn't need administrator rights."
!define MUI_COMPONENTSPAGE_NODESC
!define MUI_FINISHPAGE_RUN "$INSTDIR\Kumo.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Start Kumo"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  !if ${ARCH} == "arm64"
    ${IfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "This installer is for Windows on Arm. Use the x64 installer on this PC." /SD IDOK
      Abort
    ${EndIf}
  !else
    ${IfNot} ${IsNativeAMD64}
    ${AndIfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "Kumo needs 64-bit Windows." /SD IDOK
      Abort
    ${EndIf}
  !endif
  Call RequireValidInstallDir
  Call CheckRuntime
FunctionEnd

; Kumo's updater runs the installer with /S /relaunch after closing itself, so the new version starts on its own.
Function .onInstSuccess
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/relaunch" $R1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\Kumo.exe"'
  ${EndIf}
FunctionEnd

; Uninstall deletes the whole install folder, so the folder must be a dedicated "Kumo" folder that is either new,
; empty, or already holds Kumo. This keeps a mistyped path like C:\Users\me\Documents from ever being emptied.
; Silent installs skip the directory page, so the check also runs in .onInit and at the start of the install.
Function IsInstallDirValid
  StrCpy $R9 0
  ${GetFileName} "$INSTDIR" $0
  StrCmp $0 "Kumo" 0 done
  IfFileExists "$INSTDIR\Kumo.exe" valid
  IfFileExists "$INSTDIR\*.*" 0 valid
  FindFirst $1 $2 "$INSTDIR\*.*"
  loop:
    StrCmp $2 "" empty
    StrCmp $2 "." next
    StrCmp $2 ".." next
    FindClose $1
    Goto done
  next:
    FindNext $1 $2
    Goto loop
  empty:
    FindClose $1
  valid:
    StrCpy $R9 1
  done:
FunctionEnd

Function .onVerifyInstDir
  Call IsInstallDirValid
  StrCmp $R9 1 +2
  Abort
FunctionEnd

Function RequireValidInstallDir
  Call IsInstallDirValid
  StrCmp $R9 1 +3
  MessageBox MB_ICONSTOP "Kumo installs into its own folder named Kumo that is empty or already contains Kumo." /SD IDOK
  Abort
FunctionEnd

; Any failure or cancellation after the old version was set aside puts it back.
Function .onInstFailed
  IfFileExists "$INSTDIR.previous\Kumo.exe" 0 +4
  SetOutPath "$TEMP"
  RMDir /r "$INSTDIR"
  Rename "$INSTDIR.previous" "$INSTDIR"
FunctionEnd

Function CheckRuntime
  ; Framework-dependent build: the .NET 10 Desktop Runtime must be installed. x64 runtimes live in dotnet\x64 on Arm PCs.
  IfFileExists "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*" found
  !if ${ARCH} == "x64"
    IfFileExists "$PROGRAMFILES64\dotnet\x64\shared\Microsoft.WindowsDesktop.App\10.*" found
  !endif
  MessageBox MB_YESNO|MB_ICONINFORMATION "Kumo needs the .NET 10 Desktop Runtime, which isn't installed yet.$\r$\n$\r$\nOpen the download page now? Kumo will still be installed." /SD IDNO IDNO found
  ExecShell "open" "${RUNTIME_URL}"
  found:
FunctionEnd

; Asks a running Kumo to quit, then waits until its executable is no longer in use.
Function CloseKumo
  IfFileExists "$INSTDIR\Kumo.exe" 0 done
  ExecWait '"$INSTDIR\Kumo.exe" --exit'
  StrCpy $3 0
  check:
    ClearErrors
    FileOpen $0 "$INSTDIR\Kumo.exe" a
    IfErrors busy
    FileClose $0
    Goto done
  busy:
    IntOp $3 $3 + 1
    IntCmp $3 40 ask
    Sleep 250
    Goto check
  ask:
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Kumo is still running. Exit it from the notification area icon, then choose Retry." /SD IDCANCEL IDRETRY retry
    Abort
  retry:
    StrCpy $3 0
    Goto check
  done:
FunctionEnd

Section "Kumo" SecKumo
  SectionIn RO
  SetShellVarContext current
  Call RequireValidInstallDir
  Call CloseKumo

  ; The previous version is set aside, not deleted, until the new files are completely in place.
  SetOutPath "$TEMP"
  RMDir /r "$INSTDIR.previous"
  IfFileExists "$INSTDIR\Kumo.exe" 0 fresh
  ClearErrors
  Rename "$INSTDIR" "$INSTDIR.previous"
  IfErrors 0 fresh
  MessageBox MB_ICONSTOP "Kumo's folder is in use, so it can't be updated. Nothing was changed." /SD IDOK
  Abort
  fresh:

  SetOutPath "$INSTDIR"
  ClearErrors
  File /r "${SOURCE_DIR}\*.*"
  IfErrors 0 copied
  SetOutPath "$TEMP"
  RMDir /r "$INSTDIR"
  Rename "$INSTDIR.previous" "$INSTDIR"
  MessageBox MB_ICONSTOP "Kumo couldn't be installed. The previous version was restored." /SD IDOK
  Abort
  copied:

  FileOpen $0 "$INSTDIR\Kumo.installation.json" w
  FileWrite $0 '{"kind":"installer","version":"${VERSION}"}'
  FileClose $0
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\Kumo.lnk" "$INSTDIR\Kumo.exe"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Kumo"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "ImElio"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Kumo.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0

  SetOutPath "$TEMP"
  RMDir /r "$INSTDIR.previous"
  SetOutPath "$INSTDIR"
SectionEnd

; Silent runs are updates started by Kumo, so they keep the user's own choices about these two.
Section "Desktop shortcut" SecDesktop
  ${IfNot} ${Silent}
    CreateShortcut "$DESKTOP\Kumo.lnk" "$INSTDIR\Kumo.exe"
  ${EndIf}
SectionEnd

Section "Start with Windows" SecStartup
  ${IfNot} ${Silent}
    WriteRegStr HKCU "${RUN_KEY}" "Kumo" '"$INSTDIR\Kumo.exe" --background'
  ${EndIf}
SectionEnd

; Settings and logs in %LOCALAPPDATA%\Kumo are the user's and are kept.
Section "Uninstall"
  ExecWait '"$INSTDIR\Kumo.exe" --exit'
  Sleep 1000
  DeleteRegValue HKCU "${RUN_KEY}" "Kumo"
  Delete "$SMPROGRAMS\Kumo.lnk"
  Delete "$DESKTOP\Kumo.lnk"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  SetOutPath "$TEMP"
  IfFileExists "$INSTDIR\Kumo.installation.json" 0 +2
  RMDir /r "$INSTDIR"
SectionEnd
