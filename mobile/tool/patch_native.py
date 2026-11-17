#!/usr/bin/env python3
"""Adjusts the platform projects `flutter create` makes (CI creates them fresh each run).

  python3 tool/patch_native.py android   permissions, notification receivers, desugaring, minSdk 24, FragmentActivity
  python3 tool/patch_native.py macos     network + no sandbox (sync, updates, start at login), window size, app name
  python3 tool/patch_native.py linux     window size and title
"""
import glob
import re
import sys


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def write(p, s):
    with open(p, "w", encoding="utf-8") as f:
        f.write(s)
    print("patched", p)


def android():
    m = "android/app/src/main/AndroidManifest.xml"
    s = read(m)
    s = s.replace('android:label="coinpilot"', 'android:label="WillowWallet"')
    perms = [
        "android.permission.INTERNET",  # sync; home servers are often plain http://192.168.x.x:8484
        "android.permission.REQUEST_INSTALL_PACKAGES",  # in-app updates
        "android.permission.POST_NOTIFICATIONS",  # bill reminders
        "android.permission.RECEIVE_BOOT_COMPLETED",  # keep reminders after a restart
        "android.permission.USE_BIOMETRIC",
    ]
    for p in perms:
        if p not in s:
            s = s.replace("<application", f'<uses-permission android:name="{p}"/>\n    <application', 1)
    if "usesCleartextTraffic" not in s:
        s = s.replace("<application", '<application android:usesCleartextTraffic="true"', 1)
    receivers = """
        <receiver android:exported="false" android:name="com.dexterous.flutterlocalnotifications.ScheduledNotificationReceiver" />
        <receiver android:exported="false" android:name="com.dexterous.flutterlocalnotifications.ScheduledNotificationBootReceiver">
            <intent-filter>
                <action android:name="android.intent.action.BOOT_COMPLETED"/>
                <action android:name="android.intent.action.MY_PACKAGE_REPLACED"/>
                <action android:name="android.intent.action.QUICKBOOT_POWERON" />
                <action android:name="com.htc.intent.action.QUICKBOOT_POWERON"/>
            </intent-filter>
        </receiver>
    </application>"""
    if "ScheduledNotificationReceiver" not in s:
        s = s.replace("</application>", receivers.lstrip("\n"), 1)
    write(m, s)

    g = "android/app/build.gradle.kts"
    s = read(g)
    s = re.sub(r"minSdk\s*=\s*flutter\.minSdkVersion", "minSdk = 24", s)
    if "isCoreLibraryDesugaringEnabled" not in s:
        s = re.sub(r"compileOptions\s*\{", "compileOptions {\n        isCoreLibraryDesugaringEnabled = true", s, count=1)
    if "desugar_jdk_libs" not in s:
        s += '\ndependencies {\n    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")\n}\n'
    write(g, s)

    # local_auth shows the system fingerprint/face prompt, which needs a FragmentActivity
    for p in glob.glob("android/app/src/main/kotlin/**/MainActivity.kt", recursive=True):
        s = read(p)
        s = s.replace("import io.flutter.embedding.android.FlutterActivity", "import io.flutter.embedding.android.FlutterFragmentActivity")
        s = s.replace(": FlutterActivity()", ": FlutterFragmentActivity()")
        write(p, s)


def macos():
    for p in ["macos/Runner/Release.entitlements", "macos/Runner/DebugProfile.entitlements"]:
        write(p, """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>com.apple.security.app-sandbox</key>
	<false/>
	<key>com.apple.security.network.client</key>
	<true/>
	<key>com.apple.security.files.user-selected.read-write</key>
	<true/>
</dict>
</plist>
""")
    p = "macos/Runner/Configs/AppInfo.xcconfig"
    s = read(p)
    s = re.sub(r"PRODUCT_NAME = .*", "PRODUCT_NAME = WillowWallet", s)
    s = re.sub(r"PRODUCT_BUNDLE_IDENTIFIER = .*", "PRODUCT_BUNDLE_IDENTIFIER = com.bytehawkstudio.willowwallet", s)
    s = re.sub(r"PRODUCT_COPYRIGHT = .*", "PRODUCT_COPYRIGHT = © 2026 Bytehawk Studio", s)
    write(p, s)
    p = "macos/Runner/MainFlutterWindow.swift"
    s = read(p)
    if "setContentSize" not in s:
        s = s.replace("    self.contentViewController = flutterViewController\n",
                      "    self.contentViewController = flutterViewController\n"
                      "    self.setContentSize(NSSize(width: 1280, height: 840))\n"
                      "    self.contentMinSize = NSSize(width: 980, height: 640)\n"
                      "    self.title = \"WillowWallet\"\n"
                      "    self.center()\n", 1)
    write(p, s)
    p = "macos/Runner/Info.plist"
    s = read(p)
    if "NSFaceIDUsageDescription" not in s:
        s = s.replace("<dict>", "<dict>\n\t<key>NSFaceIDUsageDescription</key>\n\t<string>Unlock WillowWallet with Touch ID.</string>", 1)
    write(p, s)


def linux():
    for p in glob.glob("linux/**/my_application.cc", recursive=True):
        s = read(p)
        s = s.replace('"coinpilot"', '"WillowWallet"')
        s = re.sub(r"gtk_window_set_default_size\(window, \d+, \d+\);", "gtk_window_set_default_size(window, 1280, 840);", s)
        write(p, s)
    p = "linux/CMakeLists.txt"
    s = read(p)
    s = re.sub(r'set\(APPLICATION_ID "[^"]*"\)', 'set(APPLICATION_ID "com.bytehawkstudio.willowwallet")', s)
    s = re.sub(r'set\(BINARY_NAME "[^"]*"\)', 'set(BINARY_NAME "willowwallet")', s)
    write(p, s)


{"android": android, "macos": macos, "linux": linux}[sys.argv[1]]()
