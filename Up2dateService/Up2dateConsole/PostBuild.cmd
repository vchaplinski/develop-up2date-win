@echo off

set TargetDir=%1
set TargetName=%2
set TargetPath=%3
set ProjectDir=%4
set ProjectName=%5

echo Localization - CheckAndGenerate
call "%ProjectDir%Localization\CheckAndGenerate.cmd" ru-RU %TargetDir% %TargetName% %ProjectDir% %ProjectName%

if defined SignBinaryTool (
	if exist "%SignBinaryTool%" (
		echo Signing %TargetName%
		call %SignBinaryTool% %TargetPath% "%TargetName%"
		IF %ERRORLEVEL% NEQ 0 EXIT /B %ERRORLEVEL%
	)
)
echo Signing %TargetName% is not performed - SignBinaryTool is not available!
