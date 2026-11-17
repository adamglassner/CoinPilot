/// Which app this is: the phone app (Android) or the computer app (Mac, Linux), built from the same code.
/// Set once in main(); tests set it themselves (they default to the phone).
enum HostOs { android, macos, linux }

HostOs hostOs = HostOs.android;

/// The computer apps: a sidebar like WillowWallet for Windows, dialogs instead of bottom sheets.
bool get isDesktop => hostOs != HostOs.android;

/// "Android", "Mac" or "Linux" (for the sync device name, What's new and messages like "Saved on this Mac").
String get hostName => switch (hostOs) {
      HostOs.android => 'Android',
      HostOs.macos => 'Mac',
      HostOs.linux => 'Linux',
    };

/// "phone", "Mac" or "computer": "Saved on this phone".
String get deviceWord => switch (hostOs) {
      HostOs.android => 'phone',
      HostOs.macos => 'Mac',
      HostOs.linux => 'computer',
    };
