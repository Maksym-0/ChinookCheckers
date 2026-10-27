To run the engine with tablebase support you need the KingsRow endgame databases (WLD db).
Due to file size limits, they are not included in this repository.

1. Open https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm and download all files of
   "Kingsrow English 2 through 8 pieces WLD db" (Mega): Kingsrow_English_8pc_WLD_Setup.exe
   plus the Kingsrow_English_8pc_WLD_Setup-1.bin and -2.bin slices. All three must be in one folder.
2. Run the .exe and install into this directory (ChinookCheckers.Infrastructure/Engine/db/).
   The installer creates the "kr_english_wld" subfolder with db2.idx1/db2.cpr1 ... db8-*.idx1/db8-*.cpr1 (~3.4 GB).
3. The setup .exe and .bin slices can be deleted after installation.

The application finds the files automatically (it searches up to 3 levels below Engine:Databases).
If nothing is found the API still works, but without tablebase hits.
