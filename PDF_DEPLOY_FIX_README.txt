PDF deploy fix included.

What changed:
- Dockerfile now installs Linux fonts (fontconfig, DejaVu, Liberation)
- Program.cs initializes QuestPDF license once at startup
- QuestPdfGenerator uses DejaVu Sans as the PDF font family
- csproj copies optional Fonts folder on publish

Deploy steps:
1. Replace project with this zip
2. Commit and deploy
3. Generate PDF again

If your host does NOT use the Dockerfile, add DejaVuSans.ttf and DejaVuSans-Bold.ttf under Fonts/ and register them in Program.cs.
