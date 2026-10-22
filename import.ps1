$editorProject = Join-Path $PSScriptRoot "editor/platform/desktop/NoZ.Editor.Desktop.csproj"
dotnet run --project $editorProject -- import --project . @args
