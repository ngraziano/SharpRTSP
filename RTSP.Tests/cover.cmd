
IF NOT DEFINED OPENCOVER SET OPENCOVER="%ProgramFiles%\OpenCover"
IF NOT DEFINED NUNIT SET NUNIT="%ProgramFiles%\NUnit 2.5.9\bin\net-2.0"


%OPENCOVER%\OpenCover.Console.exe -register:user -target:%NUNIT%\nunit-console.exe -targetargs:"/noshadow RTSPTests.nunit"  -filter:+[*]Rtsp* -output:cover.xml

D:\temp\Report\reportGenerator "cover.xml" Coverage