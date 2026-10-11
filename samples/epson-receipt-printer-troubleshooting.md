Overview                                                                                                                                                                                                    This guide covers common troubleshooting steps for Epson receipt printers used at Girosol, including models such as:



Epson TM-T88IV

Epson TM-T88V

Epson TM-T20II

Epson TM-T20III

Epson TM-M30



This procedure is mainly intended for USB-connected Epson receipt printers using Epson Advanced Printer Driver (APD).



---



1. Identify the Issue



Before making changes, confirm the exact symptom.



Common issues include:



- Printer shows **Offline**

- Printer shows **Not Available**

- Printer appears in Windows but does not print

- Printer does not appear in Windows

- Test page does not print

- Epson APD does not detect the printer

- Printer is assigned to the wrong port

- Print jobs remain stuck in the queue

- APD installation fails

- Windows detects the USB device but no printer queue is created



---



2. Basic Physical Checks



Before troubleshooting Windows or drivers:



1. Confirm the printer is powered on.

2. Check that the printer has paper.

3. Verify there are no error lights.

4. Disconnect and reconnect the USB cable.

5. Try a different USB port on the computer.

6. If possible, test with another USB cable.

7. Power-cycle the printer:



   - Turn printer off.

   - Wait approximately 10 seconds.

   - Turn printer back on.



After reconnecting the printer, wait several seconds for Windows to detect the device.



---



3. Check Windows Printer Status



Open:



( Settings → Bluetooth & devices → Printers & scanners )



Locate the Epson printer.



Check whether the printer shows:



- Ready

- Offline

- Not Available

- Error



Open the printer queue and confirm:



- **Pause Printing** is disabled.

- **Use Printer Offline** is disabled.

- No old print jobs are stuck in the queue.



If jobs are stuck, cancel all print jobs before continuing.



---



4. Check Device Manager



Open:



 9 Device Manager )



Look under:



- Printers

- Print queues

- Universal Serial Bus controllers

- Ports (COM & LPT)

- Other devices



Check for:



- Epson TM printer devices

- Unknown USB devices

- Devices with a yellow warning symbol



If the printer appears when connected and disappears when disconnected, Windows is detecting the physical USB connection.



If Windows detects the device but the Epson printer queue does not work, the issue is likely related to the Epson driver or port configuration.



---



5. Check Epson APD Printer Configuration



Open:



  ( EPSON APD5 → Register, Change and Delete EPSON TM Printer )



Locate the installed printer.



For example:



**EPSON TM-T88V Receipt**



Select the printer and review its configuration.



### Important



For a USB-connected printer, the **Port Type should normally be USB**.



Do not use COM unless the printer is actually connected through a serial connection.



If the printer is incorrectly configured as COM:



1. Select the printer.

2. Choose **Change**.

3. Change **Port Type** to **USB**.

4. Select the Epson USB device associated with the printer.

5. Save the configuration.



The device may appear with a name similar to:



**USB TM-T88V**



After saving, test the printer again.



---



6. Print a Test Page



After verifying the port configuration:



1. Open **Printers & scanners**.

2. Select the Epson printer.

3. Open **Printer properties**.

4. Select **Print Test Page**.



If the Windows test page prints successfully, the printer hardware, driver, and Windows port configuration are functioning.



If the application still cannot print, investigate the application configuration separately.



---



7. Restart the Windows Print Spooler



If the printer appears correctly but jobs do not print, restart the Windows Print Spooler.



Open:



**Services**



Locate:



**Print Spooler**



Then:



1. Right-click **Print Spooler**.

2. Select **Restart**.



Alternatively, from an elevated Command Prompt:



```cmd

net stop spooler

net start spooler

```



After restarting the spooler, try printing again.



---



8. Clear Stuck Print Jobs



If jobs cannot be deleted normally:



1. Stop the Print Spooler.

2. Navigate to:



```text

C:\Windows\System32\spool\PRINTERS

```



3. Delete the files inside the folder.

4. Restart the Print Spooler.



Then test the printer again.



Do not delete the PRINTERS folder itself.



---



9. Reinstall Epson APD



If the printer is detected by Windows but APD cannot properly register or communicate with it, reinstall the Epson Advanced Printer Driver.



Recommended process:



1. Disconnect the Epson printer USB cable.

2. Remove the Epson printer from Windows.

3. Uninstall Epson Advanced Printer Driver.

4. Remove old Epson printer entries if necessary.

5. Restart the computer.

6. Install the correct version of Epson APD.

7. Reconnect the printer when required.

8. Register the printer again using:



**Register, Change and Delete EPSON TM Printer**



9. Set the correct model and USB port.

10. Print a test page.



---



10. Epson Port Handler Error 3006



During APD installation, the following error may appear:



**Failed to install Epson Port Handler Monitor**



**Error Code: 3006**



This can indicate an existing or corrupted Epson printer port monitor or an incomplete previous Epson APD installation.



Recommended troubleshooting:



1. Remove existing Epson APD installations.

2. Remove unused Epson printer queues.

3. Restart Windows.

4. Run the APD installer as Administrator.

5. Reinstall Epson APD.

6. Register the printer again.



If the installation continues to fail, investigate old Epson print monitor components remaining in Windows.



---



11. Confirm the Correct Printer Model



Make sure the APD configuration matches the actual physical printer.



Examples:



* TM-T88V should use the TM-T88V driver.

* TM-T20II should use the TM-T20II driver.

* TM-M30 should use the appropriate TM-M30 driver.



Using the wrong Epson model may cause printing or communication problems even when the USB connection is detected.



---



12. Quick Troubleshooting Flow



Use the following order when troubleshooting:



**Printer powered on?**



↓



**USB connection detected by Windows?**



↓



**Correct Epson printer installed?**



↓



**Printer queue online?**



↓



**Correct USB port selected in Epson APD?**



↓



**Clear print queue**



↓



**Restart Print Spooler**



↓



**Print Windows test page**



↓



**Reinstall Epson APD if necessary**



---



Common Fix



A common issue with Epson receipt printers at Girosol is that the printer is installed correctly but is assigned to the wrong port.



For USB printers, verify:



**EPSON APD → Register, Change and Delete EPSON TM Printer → Port Type → USB**



Then select the Epson USB device corresponding to the connected printer.



This should be checked before completely reinstalling the printer driver.



---



Escalation



Escalate the issue if:



* Windows does not detect the printer on multiple USB ports.

* The printer fails on multiple computers.

* The printer displays hardware error lights.

* APD installation repeatedly fails after a clean reinstall.

* The printer appears to have a damaged USB interface.

* The issue returns after successful driver and port reconfiguration.



When escalating, record:



* Printer model

* Computer name

* Windows version

* Epson APD version

* Current printer port

* Error message

* Troubleshooting steps already completed



## Import provenance
Source: Epson Receipt Printer Troubleshooting.docx. Entire document, paragraph order and wording preserved.
User-provided USB/APD procedure; not an official Epson publication.
Original SHA-256: c83490dca22049ef44baa4ff871374f2a2e4ff1abf546ab802c57d787ffae136
