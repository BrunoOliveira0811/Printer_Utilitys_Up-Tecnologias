Write-Host "Encerrando processos do app..."
Get-Process | Where-Object { $_.Name -like "*PrinterScanner*" } | ForEach-Object {
    Write-Host "  Encerrando: $($_.Name) (PID $($_.Id))"
    $_ | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

Write-Host "Removendo pastas de cache em %TEMP%..."
$temp = $env:TEMP
Get-ChildItem $temp -Directory | Where-Object { $_.Name -like "*UPUtil*" -or $_.Name -like "*PrinterScanner*" } | ForEach-Object {
    Write-Host "  Removendo: $($_.FullName)"
    Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Cache limpo. Abra o PrinterScanner.exe novamente."
Read-Host "Pressione Enter para fechar"
