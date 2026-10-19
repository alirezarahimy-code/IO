@echo off
copy *.dll %windir%\system32\
copy *.ocx %windir%\system32\

regsvr32.exe %windir%\system32\zkemkeeper.dll
regsvr32.exe %windir%\system32\Tinyx86(ver5.6.8).ocx

exit