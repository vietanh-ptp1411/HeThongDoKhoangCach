Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "FileFunc.nsh"
!define APP_NAME "Hệ thống đo lực căng Belt"
!define VERSION "1.2.1"
!define REG_KEY "Software\MVALab\BeltMeasurement"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MVALabBeltMeasurement"

Name "${APP_NAME}"
OutFile "${ReleaseDir}\BeltTensionMeasurement-Setup-${VERSION}-win-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\BeltTensionMeasurement"
InstallDirRegKey HKCU "${REG_KEY}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "1.2.1.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "MVA Lab"
VIAddVersionKey /LANG=1033 "FileDescription" "Bộ cài đặt hệ thống đo lực căng Belt - Windows x64"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "MVA Lab"

!define MUI_ABORTWARNING
!define MUI_ICON "..\Assets\BeltTensionMeasurement.ico"
!define MUI_UNICON "..\Assets\BeltTensionMeasurement.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\BeltTensionMeasurement.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Mở phần mềm đo lực căng Belt"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

!include "${PayloadInclude}"

Function .onInit
  SetShellVarContext current
  SetRegView 64
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Bản cài này dành cho Windows 64-bit."
    SetErrorLevel 2
    Quit
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "Cần Windows 10 hoặc mới hơn. Khuyến nghị Windows 11."
    SetErrorLevel 2
    Quit
  ${EndIf}
FunctionEnd

Function CheckApplicationClosed
  ; Bản cũ có tên executable khác nhưng dùng cùng khóa InstallDir khi nâng cấp.
  IfFileExists "$INSTDIR\HeThongDoKhoangCach.exe" legacy_check legacy_done
  legacy_check:
    ClearErrors
    FileOpen $0 "$INSTDIR\HeThongDoKhoangCach.exe" a
    IfErrors legacy_busy
    FileClose $0
    Goto legacy_done
  legacy_busy:
    IfSilent fail
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Vui lòng đóng phiên bản cũ trước khi nâng cấp." IDRETRY legacy_check
    Goto fail
  legacy_done:
  IfFileExists "$INSTDIR\BeltTensionMeasurement.exe" check_file done
  check_file:
    ClearErrors
    FileOpen $0 "$INSTDIR\BeltTensionMeasurement.exe" a
    IfErrors busy
    FileClose $0
    Goto done
  busy:
    IfSilent fail
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Vui lòng đóng phần mềm đo trước khi cài đặt hoặc nâng cấp." IDRETRY check_file
  fail:
    SetErrorLevel 3
    Abort
  done:
FunctionEnd

Section "Ứng dụng và hướng dẫn sử dụng" SEC_APP
  SectionIn RO
  Call CheckApplicationClosed
  ; Chỉ gỡ các binary mang tên cũ; giữ nguyên appsettings.json và Data.
  ClearErrors
  Delete "$INSTDIR\HeThongDoKhoangCach.exe"
  Delete "$INSTDIR\HeThongDoKhoangCach.dll"
  Delete "$INSTDIR\HeThongDoKhoangCach.deps.json"
  Delete "$INSTDIR\HeThongDoKhoangCach.runtimeconfig.json"
  Delete "$INSTDIR\HeThongDoKhoangCach.pdb"
  IfErrors 0 legacy_removed
    MessageBox MB_ICONSTOP "Không xóa được tệp chương trình cũ. Đóng ứng dụng rồi cài lại." /SD IDOK
    SetErrorLevel 3
    Abort
  legacy_removed:
  SetOverwrite on
  !insertmacro InstallPayload
  SetOutPath "$INSTDIR\HuongDan"
  File "${DocumentsDir}\HuongDanSuDung.pdf"
  File "${DocumentsDir}\HuongDanSuDung.docx"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${REG_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "MVA Lab"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\BeltTensionMeasurement.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $1 $2 $3
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $1
SectionEnd

Section "Lối tắt trên Desktop và Start Menu" SEC_SHORTCUTS
  SetOutPath "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\BeltTensionMeasurement.exe"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Hướng dẫn sử dụng.lnk" "$INSTDIR\HuongDan\HuongDanSuDung.pdf"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Gỡ cài đặt.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\BeltTensionMeasurement.exe"
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  IfFileExists "$INSTDIR\BeltTensionMeasurement.exe" 0 done
  check_file:
    ClearErrors
    FileOpen $0 "$INSTDIR\BeltTensionMeasurement.exe" a
    IfErrors busy
    FileClose $0
    Goto done
  busy:
    IfSilent fail
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Vui lòng đóng phần mềm đo trước khi gỡ cài đặt." IDRETRY check_file
  fail:
    SetErrorLevel 3
    Abort
  done:
FunctionEnd

Section "Uninstall"
  ; Chỉ xóa đúng các tệp chương trình đã đóng gói; không xóa dữ liệu người dùng.
  !insertmacro UninstallPayload
  Delete "$INSTDIR\HuongDan\HuongDanSuDung.pdf"
  Delete "$INSTDIR\HuongDan\HuongDanSuDung.docx"
  RMDir "$INSTDIR\HuongDan"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Hướng dẫn sử dụng.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Gỡ cài đặt.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${REG_KEY}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
