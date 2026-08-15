Set sh = CreateObject("WScript.Shell")
' 0 = hidden window, False = do not wait
sh.Run "cmd /c ""D:\MakeGame\push.bat""", 0, False
