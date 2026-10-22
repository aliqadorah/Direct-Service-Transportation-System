# System email through Microsoft 365 — owner's setup guide

_22 Oct 2026 · DT-250…DT-253 · closes DEF-02 for email (the payment gateway is still open)_

The application sends its emails (sign-up confirmation, password reset, invitations, email change, two-step notices, support-access notices, staff alerts) **from one Microsoft 365 mailbox, `no-reply@directservicetech.com`**, through **Microsoft Graph**. It signs in as an **app registered in Microsoft Entra** with a **client secret** — no SMTP, and no mailbox password anywhere. The app is allowed to send **only** as that one mailbox.

Until you finish these steps the application keeps its development mode (emails saved as files on the server, nothing sent). Nothing changes for users while you work.

You need: a Microsoft 365 **Global Administrator** (or Exchange Administrator + Application Administrator) account for `directservicetech.com`, access to the domain's **DNS**, and your platform staff account in the provider console (with your authenticator app).

Commands are for **PowerShell 7** on your PC. Install the two modules once:

```powershell
Install-Module ExchangeOnlineManagement -Scope CurrentUser
Install-Module Microsoft.Graph -Scope CurrentUser
```

---

## Step 1 — Create the shared mailbox `no-reply@directservicetech.com` and block its sign-in

A shared mailbox needs no licence and has no password of its own.

```powershell
Connect-ExchangeOnline -UserPrincipalName admin@directservicetech.com
New-Mailbox -Shared -Name "No Reply" -DisplayName "Direct Service" -Alias "no-reply" -PrimarySmtpAddress "no-reply@directservicetech.com"
```

(Or: Exchange admin center → **Recipients → Mailboxes → Add a shared mailbox**.)

Block sign-in to the mailbox's account (nobody should ever sign in to it):

```powershell
Connect-MgGraph -Scopes "User.ReadWrite.All"
Update-MgUser -UserId "no-reply@directservicetech.com" -AccountEnabled:$false
```

(Or: Microsoft 365 admin center → **Users → Active users → No Reply → Block sign-in**.)

---

## Step 2 — Register the app in Entra and note its two IDs

1. Open **https://entra.microsoft.com** → **Identity → Applications → App registrations → New registration**.
2. Name: `Direct Service Transportation – System email`. Supported account types: **Accounts in this organizational directory only**. Redirect URI: leave empty. **Register**.
3. On the app's **Overview** page write down:
   - **Application (client) ID** → the console's *Client ID*
   - **Directory (tenant) ID** → the console's *Tenant ID*
4. Open **Identity → Applications → Enterprise applications**, find the same app, and write down its **Object ID** (this is the *service principal* — not the Object ID shown on the app registration page). You need it in step 3.

**Do not** add the Microsoft Graph permission *Mail.Send* under **API permissions**. That permission lets the app send as **every** mailbox in the company; step 3 gives it the right for one mailbox only.

---

## Step 3 — Allow the app to send as `no-reply@` only (Exchange Online RBAC for Applications)

This is Microsoft's current method (it replaces the older "application access policies").

```powershell
Connect-ExchangeOnline -UserPrincipalName admin@directservicetech.com

# 1. Make the Entra app known to Exchange (AppId = Client ID, ObjectId = the Enterprise application's Object ID from step 2.4)
New-ServicePrincipal -AppId "<CLIENT-ID>" -ObjectId "<ENTERPRISE-APP-OBJECT-ID>" -DisplayName "DST System email"

# 2. A scope that contains only the no-reply mailbox
New-ManagementScope -Name "DST no-reply mailbox" -RecipientRestrictionFilter "PrimarySmtpAddress -eq 'no-reply@directservicetech.com'"

# 3. The right to send mail, limited to that scope
New-ManagementRoleAssignment -App "<CLIENT-ID>" -Role "Application Mail.Send" -CustomResourceScope "DST no-reply mailbox"
```

### Remove any tenant-wide grant — otherwise the restriction does not apply

Entra permissions and Exchange role assignments **add up**: if the app also has the Entra permission *Mail.Send (Application)* it can still send as anyone. Check and remove it:

1. Entra → **App registrations → the app → API permissions**. If **Microsoft Graph · Mail.Send · Application** is listed: open **⋯ → Revoke admin consent**, then **⋯ → Remove permission**.
2. Make sure no consent is left on the service principal:

```powershell
Connect-MgGraph -Scopes "Application.ReadWrite.All","AppRoleAssignment.ReadWrite.All"
$sp    = Get-MgServicePrincipal -Filter "appId eq '<CLIENT-ID>'"
$graph = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
$send  = $graph.AppRoles | Where-Object { $_.Value -eq "Mail.Send" }
Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $sp.Id |
    Where-Object { $_.AppRoleId -eq $send.Id } |
    ForEach-Object { Remove-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $sp.Id -AppRoleAssignmentId $_.Id }
```

3. If an old application access policy exists for this app, remove it (`Get-ApplicationAccessPolicy`, then `Remove-ApplicationAccessPolicy -Identity <id>`).

### Check it

```powershell
# Must say InScope = True for the role "Application Mail.Send"
Test-ServicePrincipalAuthorization -Identity "<CLIENT-ID>" -Resource "no-reply@directservicetech.com"

# Must say InScope = False (any other mailbox, e.g. yours)
Test-ServicePrincipalAuthorization -Identity "<CLIENT-ID>" -Resource "admin@directservicetech.com"
```

Changes can take **30 minutes to 2 hours** to reach Microsoft Graph. The console's test email (step 4) also tells you if a tenant-wide grant is still there ("The app has the tenant-wide Mail.Send permission…").

---

## Step 4 — Create the client secret and enter it in the console

1. Entra → **App registrations → the app → Certificates & secrets → Client secrets → New client secret**. Description: `DST system email 2026-10`. Expires: **12 months** (Microsoft allows at most 24).
2. **Copy the Value at once** (it is shown only once — not the "Secret ID"). Note the **Expires** date.
3. Provider console → **System email**:
   - How emails are sent: **Microsoft 365**
   - Tenant ID, Client ID (step 2), Client secret (the Value), Secret expires on (the date from Entra)
   - Sender mailbox: `no-reply@directservicetech.com`; Sender name: e.g. `Direct Service`; Reply-to: e.g. `support@directservicetech.com` (optional — where replies go)
   - **Save settings** → enter the current code from your authenticator app.
4. The new settings are now **pending** (the old ones still send). Type your own address under **Test email** → **Send test email**.
   - Success: "…the new settings are now in use". Check that the email arrived (also in Junk).
   - Failure: the screen says why in plain words (wrong secret, missing permission, mailbox not allowed for this app, mailbox or domain not in this Microsoft 365, …) with the technical detail underneath. Fix it and test again — nothing changes for users until a test succeeds.

The secret is stored encrypted and is never shown again. The server's Data Protection keys must be kept (they are a hosting setting): if they are lost, enter the secret again.

---

## Step 5 — SPF, DKIM and DMARC for `directservicetech.com`

Without these, emails go to Junk or are refused.

**SPF** — one TXT record on `directservicetech.com` (merge with an existing `v=spf1` record; there may be only one):

```
v=spf1 include:spf.protection.outlook.com -all
```

If another service also sends mail for the domain (the website's contact form, a newsletter tool), add its `include:` before `-all`.

**DKIM** — signs every email:

```powershell
Connect-ExchangeOnline -UserPrincipalName admin@directservicetech.com
New-DkimSigningConfig -DomainName "directservicetech.com" -Enabled $false
Get-DkimSigningConfig -Identity "directservicetech.com" | Format-List Selector1CNAME, Selector2CNAME
```

Add the two CNAME records it shows (copy them exactly), at your DNS provider:

| Host | Type | Points to |
|---|---|---|
| `selector1._domainkey` | CNAME | the `Selector1CNAME` value |
| `selector2._domainkey` | CNAME | the `Selector2CNAME` value |

When DNS has them (minutes to a day): `Set-DkimSigningConfig -Identity "directservicetech.com" -Enabled $true`.
(Or: Microsoft Defender portal → **Email & collaboration → Policies & rules → Threat policies → Email authentication settings → DKIM**.)

**DMARC** — TXT record on `_dmarc.directservicetech.com`. Start by only collecting reports:

```
v=DMARC1; p=none; rua=mailto:dmarc-reports@directservicetech.com
```

After 2–4 weeks of reports showing only your own mail passing, tighten it to `p=quarantine`, later `p=reject`. (The `rua` mailbox must exist; it can be a shared mailbox.)

Check with `Resolve-DnsName -Type TXT directservicetech.com`, `Resolve-DnsName -Type TXT _dmarc.directservicetech.com`, and send a test email to a Gmail address: **Show original** must say SPF, DKIM and DMARC **PASS**.

---

## Sending limits

- Exchange Online: **30 messages a minute** and **10,000 recipients a day** per mailbox; at most 500 recipients per message (the application sends one recipient per email).
- Microsoft Graph throttles per app and mailbox; when it answers "429 Too Many Requests" with *Retry-After*, the application waits that long and tries again.
- The application never sends more than **30 emails a minute** (setting `Email__PerMinute`), retries failures after 1 min, 5 min, 15 min, 1 h, 4 h and 12 h, then marks the email **Failed** and emails platform staff (at most once in 6 hours); the console shows it in the delivery log with **Resend**. At most **5 emails of the same kind to the same address an hour** are sent (protection against someone requesting password resets for another person over and over).
- Sent emails are kept in the shared mailbox's **Sent Items** (50 GB free for a shared mailbox). Add a retention policy if you do not want to keep them.
- The server needs outbound HTTPS to `login.microsoftonline.com` and `graph.microsoft.com`.

---

## Rotating the client secret

The console and an email warn platform staff **30 days** and again **7 days** before the date you entered.

1. Entra → the app → **Certificates & secrets → New client secret** (the old one keeps working meanwhile). Copy the Value and the new expiry date.
2. Console → System email → enter the new secret and the new **Secret expires on** → **Save settings** (authenticator code) → **Send test email**. On success the new secret is in use.
3. Back in Entra, **delete the old secret**.

---

## Changing the mailbox or the domain later

**Another mailbox or domain in the same Microsoft 365:**
1. If the domain is new: Microsoft 365 admin center → **Settings → Domains → Add domain**, verify it with the TXT record shown, and add the DNS records the wizard lists.
2. Create the shared mailbox and block its sign-in (step 1, with the new address).
3. Allow the app to use it — change the scope (during the switch both addresses may be in the filter):
   ```powershell
   Set-ManagementScope -Identity "DST no-reply mailbox" -RecipientRestrictionFilter "PrimarySmtpAddress -eq 'no-reply@newdomain.com'"
   Test-ServicePrincipalAuthorization -Identity "<CLIENT-ID>" -Resource "no-reply@newdomain.com"
   ```
4. SPF, DKIM and DMARC for the new domain (step 5).
5. Console → change **Sender mailbox** → Save (code) → Send test email → on success it is in use.

**A different Microsoft 365 (another tenant):** do steps 1–5 there (new mailbox, new app registration, new scope, new secret, DNS for its domain), then in the console enter the new Tenant ID, Client ID, secret, expiry date and mailbox → Save → Send test email. The old settings keep sending until the test succeeds. Afterwards delete the old app registration's secret.

**Going back to the development mode** (no emails leave the server) is the same: choose Development → Save → Send test email.
