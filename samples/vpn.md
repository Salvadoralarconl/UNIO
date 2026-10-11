# VPN Troubleshooting Guide
Version: 2.1 | IT Operations | Fictional portfolio sample

## Section 3.2 — Internal Service Access
If you are connected to the VPN but cannot access internal services, confirm your assigned VPN profile and check internal DNS resolution. Internal services require the Corporate Access profile. Verify the active profile in your VPN client settings; if it differs from your assigned profile, contact IT before changing it.

While connected, run nslookup with the internal service hostname supplied by IT. Record the returned DNS server, any error, and whether the hostname resolves. Do not substitute public DNS for internal DNS or disable security software. If the profile is correct and resolution fails, record the result and escalate to IT.

After the assigned profile and DNS resolution are verified, test the internal service again. Record the exact error and time if access still fails.
