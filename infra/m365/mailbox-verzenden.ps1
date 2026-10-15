<#
.SYNOPSIS
  Laat de API (managed identity van app-dvd-api-prod) mailings versturen vanuit de mailboxen voorzitter@ en secretaris@ in
  Microsoft 365, en zet DKIM aan voor Microsoft 365 (runbook docs/runbooks/mailing-via-mailbox.md).

.DESCRIPTION
  Gebruikt Exchange "RBAC for Applications": de app krijgt de rol "Application Mail.Send" ALLEEN voor de opgegeven
  mailboxen. Geef de managed identity dus GEEN Mail.Send in Entra (dat zou voor alle mailboxen gelden).

  Stap 1 (rechten):   pwsh infra/m365/mailbox-verzenden.ps1
  Stap 2 (DKIM):      pwsh infra/m365/mailbox-verzenden.ps1 -Dkim          → toont twee CNAME-records voor Cloud86
  Stap 3 (DKIM aan):  pwsh infra/m365/mailbox-verzenden.ps1 -Dkim -Enable  → nadat de CNAME-records er staan

  Nodig: PowerShell 7 (brew install powershell), de module ExchangeOnlineManagement
  (Install-Module ExchangeOnlineManagement -Scope CurrentUser), Azure CLI ingelogd (az login), een beheerdersaccount
  van Microsoft 365.
#>
param(
    [string[]] $Mailboxes = @('voorzitter@vrolijkedrammers.nl', 'secretaris@vrolijkedrammers.nl', 'dansgardefestival@vrolijkedrammers.nl'),
    [string] $WebApp = 'app-dvd-api-prod',
    [string] $ResourceGroup = 'rg-dvd-prod',
    [string] $Domain = 'vrolijkedrammers.nl',
    [switch] $Dkim,
    [switch] $Enable
)

$ErrorActionPreference = 'Stop'
$scopeName = 'DVD mailing afzenders'

Import-Module ExchangeOnlineManagement
Connect-ExchangeOnline -ShowBanner:$false

if ($Dkim) {
    $config = Get-DkimSigningConfig -Identity $Domain -ErrorAction SilentlyContinue
    if (-not $config) {
        New-DkimSigningConfig -DomainName $Domain -Enabled $false | Out-Null
        $config = Get-DkimSigningConfig -Identity $Domain
    }

    if ($Enable) {
        Set-DkimSigningConfig -Identity $Domain -Enabled $true
        Write-Host "DKIM voor Microsoft 365 staat aan voor $Domain." -ForegroundColor Green
    }
    else {
        Write-Host "Zet deze twee CNAME-records bij Cloud86 (DNS van $Domain):" -ForegroundColor Cyan
        Write-Host "  selector1._domainkey  →  $($config.Selector1CNAME)"
        Write-Host "  selector2._domainkey  →  $($config.Selector2CNAME)"
        Write-Host "Draai daarna (na een uur of zo):  pwsh infra/m365/mailbox-verzenden.ps1 -Dkim -Enable"
        Write-Host "De bestaande records (SPF, DMARC en de azurecomm-DKIM) blijven staan."
    }

    Disconnect-ExchangeOnline -Confirm:$false
    return
}

# De managed identity van de API: object-id en app-id.
$principalId = az webapp identity show --resource-group $ResourceGroup --name $WebApp --query principalId -o tsv
if (-not $principalId) { throw "Geen managed identity gevonden voor $WebApp." }
$appId = az ad sp show --id $principalId --query appId -o tsv
Write-Host "Managed identity: $WebApp (app-id $appId)"

foreach ($mailbox in $Mailboxes) {
    if (-not (Get-EXOMailbox -Identity $mailbox -ErrorAction SilentlyContinue)) {
        throw "$mailbox is geen mailbox in Microsoft 365 (een alias of doorstuuradres kan niet versturen)."
    }
}

# Kleine organisaties staan in Exchange Online in een compacte modus waarin New-ManagementScope niet mag.
if ((Get-OrganizationConfig).IsDehydrated) {
    Enable-OrganizationCustomization
    Write-Host 'Organisatie-aanpassingen aangezet. Dit kan tot een uur duren; draai het script daarna opnieuw.' -ForegroundColor Yellow
    Disconnect-ExchangeOnline -Confirm:$false
    return
}

if (-not (Get-ServicePrincipal -Identity $appId -ErrorAction SilentlyContinue)) {
    New-ServicePrincipal -AppId $appId -ObjectId $principalId -DisplayName "DVD API ($WebApp)" | Out-Null
}

$filter = ($Mailboxes | ForEach-Object { "PrimarySmtpAddress -eq '$_'" }) -join ' -or '
if (Get-ManagementScope -Identity $scopeName -ErrorAction SilentlyContinue) {
    Set-ManagementScope -Identity $scopeName -RecipientRestrictionFilter $filter
}
else {
    New-ManagementScope -Name $scopeName -RecipientRestrictionFilter $filter | Out-Null
}

$existing = Get-ManagementRoleAssignment -RoleAssignee $appId -Role 'Application Mail.Send' -ErrorAction SilentlyContinue
if (-not $existing) {
    New-ManagementRoleAssignment -App $appId -Role 'Application Mail.Send' -CustomResourceScope $scopeName | Out-Null
}

foreach ($mailbox in $Mailboxes) {
    $test = Test-ServicePrincipalAuthorization -Identity $appId -Resource $mailbox | Where-Object RoleName -eq 'Application Mail.Send'
    Write-Host ("{0}: {1}" -f $mailbox, $(if ($test.InScope) { 'mag versturen' } else { 'NIET toegestaan' }))
}

Write-Host ''
Write-Host 'Klaar. Zet in GitHub de variabele DVD_EMAIL_MAILBOX_SENDERS=voorzitter,secretaris en draai de Deploy (prod).' -ForegroundColor Green
Write-Host 'Rechten in Exchange kunnen tot een uur nodig hebben voordat ze werken; tot die tijd gaat de mail via Azure.'
Disconnect-ExchangeOnline -Confirm:$false
