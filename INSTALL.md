# Setting up Rafter Cat's Spot Light

Same steps as the Chore List and Looking Glass.

1. On GitHub (as The-Kinship), make a new **public** repo called `spot-light`. Leave it empty.
2. Click **uploading an existing file**, drag in everything from this zip, commit.
3. The `.github` folder usually gets skipped. If so: **Add file > Create new file**, name it
   `.github/workflows/build.yml`, paste in that file's contents (open it from the unzipped folder in Notepad), commit.
4. **Settings > Actions > General > Workflow permissions**: **Read and write permissions**, Save.
5. **Actions** tab: wait for "Build plugin" to go green (or press **Run workflow**).
6. In game: `/xlsettings` > **Experimental** > add
   `https://raw.githubusercontent.com/The-Kinship/spot-light/main/repo.json`, press +, Save.
7. `/xlplugins`, search **Spot Light**, Install. Type `/spotlight`.

Try it: right-click Tiger Skin in your bags > "Spot Light: mark as important". It should appear in the
/spotlight list. Search "gazelle" in the window and press Add. Remove one from the list.
