#!/usr/bin/env bash
# Interactive helper for building the MAUI app: installs a user-local Android
# SDK (no root) and/or runs a Debug build. Re-runnable; it asks before doing anything.
set -euo pipefail

cd "$(dirname "$0")/.."

DEFAULT_SDK="${ANDROID_HOME:-$HOME/android-sdk}"
DEFAULT_JDK="/usr/lib/jvm/java-17-openjdk-amd64"   # Android tooling wants JDK 17, not the system default

ask() {  # ask "prompt" "default" -> echoes answer
  local reply
  read -r -p "$1 [$2]: " reply
  echo "${reply:-$2}"
}

confirm() {  # confirm "prompt" -> returns 0 on yes, default no
  local reply
  read -r -p "$1 [y/N]: " reply
  [[ "$reply" =~ ^[Yy]$ ]]
}

echo "Morning Gateway - Android build setup"
echo
echo "  1) Install the Android SDK, then build"
echo "  2) Build only (SDK already installed)"
echo "  3) Quit"
echo
read -r -p "What do you want to do? [1]: " choice
choice="${choice:-1}"

case "$choice" in
  1|2) ;;
  *) echo "Nothing done."; exit 0 ;;
esac

SDK="$(ask "Android SDK directory" "$DEFAULT_SDK")"
JDK="$(ask "JDK 17 directory" "$DEFAULT_JDK")"
[[ -x "$JDK/bin/java" ]] || { echo "No java found at $JDK/bin/java"; exit 1; }

COMMON=(-f net10.0-android "-p:AndroidSdkDirectory=$SDK" "-p:JavaSdkDirectory=$JDK")

if [[ "$choice" == "1" ]]; then
  echo
  echo "This downloads the Android SDK into $SDK and accepts Google's Android SDK"
  echo "license terms (https://developer.android.com/studio/terms) on your behalf."
  confirm "Continue?" || { echo "Cancelled."; exit 0; }
  dotnet build src/MorningGateway "${COMMON[@]}" -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True
fi

echo
if confirm "Run a Debug build now?"; then
  dotnet build src/MorningGateway "${COMMON[@]}"
fi

cat <<MSG

To build later without this script:
  dotnet build src/MorningGateway -f net10.0-android -p:AndroidSdkDirectory="$SDK" -p:JavaSdkDirectory="$JDK"
(or export ANDROID_HOME="$SDK" and JAVA_HOME="$JDK" in your shell profile)
MSG
