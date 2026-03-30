# Setup-Git.ps1
# Initialize git repository and create first commit for FollowMe project
# Run this in Windows PowerShell from the followme-dev project root

Write-Host "FollowMe Git Setup" -ForegroundColor Cyan
Write-Host "=================" -ForegroundColor Cyan
Write-Host ""

# Check if git is available
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "Error: git is not installed or not in PATH" -ForegroundColor Red
    Write-Host "Download from: https://git-scm.com/download/win" -ForegroundColor Yellow
    exit 1
}

# Check if we're in the right directory
if (-not (Test-Path "DesktopDotNet\FollowMe.sln") -or -not (Test-Path "Core\Config.lua")) {
    Write-Host "Error: Not in the followme-dev directory" -ForegroundColor Red
    Write-Host "Run this script from: followme-dev\" -ForegroundColor Yellow
    exit 1
}

# Initialize repo
Write-Host "Initializing git repository..." -ForegroundColor Green
git init -b main
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error initializing git repository" -ForegroundColor Red
    exit 1
}

# Create .gitignore if it doesn't exist
if (-not (Test-Path ".gitignore")) {
    Write-Host "Creating .gitignore..." -ForegroundColor Green
    @"
# Build artifacts
bin/
obj/
*.dll
*.exe
*.pdb

# Visual Studio
.vs/
*.user
*.sln.docstates
*.suo

# Temp files
*.tmp
~`$*

# OS
Thumbs.db
desktop.ini
.DS_Store

# OneDrive
~*
"@ | Set-Content ".gitignore"
}

# Stage all files
Write-Host "Staging files..." -ForegroundColor Green
git add .

# Create first commit
Write-Host "Creating initial commit..." -ForegroundColor Green
git commit -m "Initial: FollowMe v0.1.0 — optical telemetry bridge for RIFT multiboxing"

if ($LASTEXITCODE -eq 0) {
    Write-Host "" -ForegroundColor Green
    Write-Host "Success! Git repository initialized." -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Create a repository on GitHub: https://github.com/new" -ForegroundColor White
    Write-Host ""
    Write-Host "  2. Add the remote:" -ForegroundColor White
    Write-Host "     git remote add origin https://github.com/YOUR_USERNAME/followme.git" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  3. Push to GitHub:" -ForegroundColor White
    Write-Host "     git push -u origin main" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host "Error creating initial commit" -ForegroundColor Red
    exit 1
}
