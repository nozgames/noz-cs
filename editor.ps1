$editorProject = Join-Path $PSScriptRoot "editor/NoZ.Editor.csproj"
dotnet run --project $editorProject -c Release -- --project .
