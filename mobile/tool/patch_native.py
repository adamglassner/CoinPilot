#!/usr/bin/env python3
"""Adjusts the platform projects `flutter create` makes (CI creates them fresh each run).

  python3 tool/patch_native.py android   permissions, notification receivers, desugaring, minSdk 24, FragmentActivity
  python3 tool/patch_native.py macos     network + no sandbox (sync, updates, start at login), window size, app name
  python3 tool/patch_native.py linux     window size and title
"""
import glob
import os
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

    home_widget_android()

    # Text recognition only uses the Latin model; the other scripts' classes are optional
    write("android/app/proguard-rules.pro", """-dontwarn com.google.mlkit.vision.text.chinese.**
-dontwarn com.google.mlkit.vision.text.devanagari.**
-dontwarn com.google.mlkit.vision.text.japanese.**
-dontwarn com.google.mlkit.vision.text.korean.**
""")

    # local_auth shows the system fingerprint/face prompt, which needs a FragmentActivity
    for p in glob.glob("android/app/src/main/kotlin/**/MainActivity.kt", recursive=True):
        s = read(p)
        s = s.replace("import io.flutter.embedding.android.FlutterActivity", "import io.flutter.embedding.android.FlutterFragmentActivity")
        s = s.replace(": FlutterActivity()", ": FlutterFragmentActivity()")
        write(p, s)


def home_widget_android():
    """The home screen widget: safe to spend, left to spend this month, and a + to add a transaction."""
    m = "android/app/src/main/AndroidManifest.xml"
    s = read(m)
    if "SpendWidget" not in s:
        s = s.replace("</application>", """    <receiver android:name=".SpendWidget" android:exported="false" android:label="WillowWallet">
            <intent-filter>
                <action android:name="android.appwidget.action.APPWIDGET_UPDATE" />
            </intent-filter>
            <meta-data android:name="android.appwidget.provider" android:resource="@xml/spend_widget_info" />
        </receiver>
    </application>""", 1)
        # Tapping the widget opens the app (home_widget's launch action)
        s = s.replace("</activity>", """    <intent-filter>
                <action android:name="es.antonborri.home_widget.action.LAUNCH" />
            </intent-filter>
        </activity>""", 1)
    write(m, s)

    res = "android/app/src/main/res"
    os.makedirs(f"{res}/layout", exist_ok=True)
    os.makedirs(f"{res}/xml", exist_ok=True)
    os.makedirs(f"{res}/drawable", exist_ok=True)
    write(f"{res}/drawable/spend_widget_bg.xml", """<?xml version="1.0" encoding="utf-8"?>
<shape xmlns:android="http://schemas.android.com/apk/res/android" android:shape="rectangle">
    <solid android:color="#14211B" />
    <corners android:radius="24dp" />
</shape>
""")
    write(f"{res}/drawable/spend_widget_add.xml", """<?xml version="1.0" encoding="utf-8"?>
<shape xmlns:android="http://schemas.android.com/apk/res/android" android:shape="oval">
    <solid android:color="#5DD39E" />
</shape>
""")
    write(f"{res}/layout/spend_widget.xml", """<?xml version="1.0" encoding="utf-8"?>
<LinearLayout xmlns:android="http://schemas.android.com/apk/res/android"
    android:id="@+id/widget_root"
    android:layout_width="match_parent"
    android:layout_height="match_parent"
    android:background="@drawable/spend_widget_bg"
    android:orientation="horizontal"
    android:padding="16dp">
    <LinearLayout
        android:layout_width="0dp"
        android:layout_height="match_parent"
        android:layout_weight="1"
        android:gravity="center_vertical"
        android:orientation="vertical">
        <TextView
            android:id="@+id/widget_safe_label"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:text="Safe to spend"
            android:textColor="#9FB8AC"
            android:textSize="13sp" />
        <TextView
            android:id="@+id/widget_safe"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:text="—"
            android:textColor="#FFFFFF"
            android:textSize="30sp"
            android:textStyle="bold" />
        <TextView
            android:id="@+id/widget_left"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:maxLines="2"
            android:text="Open WillowWallet to set up"
            android:textColor="#5DD39E"
            android:textSize="12.5sp" />
    </LinearLayout>
    <TextView
        android:id="@+id/widget_add"
        android:layout_width="48dp"
        android:layout_height="48dp"
        android:layout_gravity="center_vertical"
        android:background="@drawable/spend_widget_add"
        android:contentDescription="Add transaction"
        android:gravity="center"
        android:text="+"
        android:textColor="#0B120F"
        android:textSize="28sp" />
</LinearLayout>
""")
    write(f"{res}/xml/spend_widget_info.xml", """<?xml version="1.0" encoding="utf-8"?>
<appwidget-provider xmlns:android="http://schemas.android.com/apk/res/android"
    android:minWidth="180dp"
    android:minHeight="80dp"
    android:targetCellWidth="3"
    android:targetCellHeight="1"
    android:updatePeriodMillis="1800000"
    android:initialLayout="@layout/spend_widget"
    android:previewLayout="@layout/spend_widget"
    android:resizeMode="horizontal|vertical"
    android:widgetCategory="home_screen" />
""")
    for main in glob.glob("android/app/src/main/kotlin/**/MainActivity.kt", recursive=True):
        folder = os.path.dirname(main)
        package = re.search(r"^package\s+([\w.]+)", read(main), re.M).group(1)
        write(os.path.join(folder, "SpendWidget.kt"), f"""package {package}

import android.appwidget.AppWidgetManager
import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import android.widget.RemoteViews
import es.antonborri.home_widget.HomeWidgetLaunchIntent
import es.antonborri.home_widget.HomeWidgetProvider

/** The home screen widget. WillowWallet saves the numbers (home_widget) whenever your data changes. */
class SpendWidget : HomeWidgetProvider() {{
    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray, widgetData: SharedPreferences) {{
        appWidgetIds.forEach {{ id ->
            val views = RemoteViews(context.packageName, R.layout.spend_widget).apply {{
                setTextViewText(R.id.widget_safe_label, widgetData.getString("safeLabel", "Safe to spend"))
                setTextViewText(R.id.widget_safe, widgetData.getString("safe", "—"))
                setTextViewText(R.id.widget_left, widgetData.getString("left", "Open WillowWallet to set up"))
                setOnClickPendingIntent(R.id.widget_root, HomeWidgetLaunchIntent.getActivity(context, MainActivity::class.java, Uri.parse("willowwallet://open")))
                setOnClickPendingIntent(R.id.widget_add, HomeWidgetLaunchIntent.getActivity(context, MainActivity::class.java, Uri.parse("willowwallet://add")))
            }}
            appWidgetManager.updateAppWidget(id, views)
        }}
    }}
}}
""")
        print("wrote", os.path.join(folder, "SpendWidget.kt"))


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
