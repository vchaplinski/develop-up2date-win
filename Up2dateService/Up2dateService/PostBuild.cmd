@echo off

set TargetName=%1
set TargetPath=%2

if defined SignBinaryTool (
	if exist "%SignBinaryTool%" (
		echo Signing %TargetName%
		call %SignBinaryTool% %TargetPath% "%TargetName%"
		IF %ERRORLEVEL% NEQ 0 EXIT /B %ERRORLEVEL%
	)
)
echo Signing %TargetName% is not performed - SignBinaryTool is not available!
