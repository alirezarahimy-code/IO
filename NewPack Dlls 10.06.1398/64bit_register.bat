@echo off
copy *.dll %windir%\syswow64\
copy *.dll %windir%\system32\
copy *.ocx %windir%\syswow64\
copy *.ocx %windir%\system32\

regsvr32.exe %windir%\syswow64\zkemkeeper.dll
regsvr32.exe %windir%\syswow64\Tinyx86(ver5.6.8).ocx
regsvr32.exe %windir%\system32\Tinyx64(ver5.6.8).ocx

exit