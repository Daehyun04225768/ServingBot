import socket
import time
import urllib.request
import tkinter as tk
from tkinter import messagebox
import webbrowser
import sys
import os
import subprocess

# 설정
CURRENT_VERSION = "1.0.0"
VERSION_CHECK_URL = "https://raw.githubusercontent.com/Daehyun04225768/ServingBot/main/version.txt"
RELEASE_PAGE_URL = "https://github.com/Daehyun04225768/ServingBot/releases"

class ServingRobot:
    def __init__(self, ip='127.0.0.1', port=5555):
        print(f"[{self.__class__.__name__}] Initializing...")
        self._check_for_updates()
        
        self.ip = ip
        self.port = port
        self.sock = None
        
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.connect((self.ip, self.port))
            print(f"[{self.__class__.__name__}] Connected to Unity Simulator at {self.ip}:{self.port}")
        except ConnectionRefusedError:
            print(f"[ERROR] Could not connect to Unity server at {self.ip}:{self.port}.")
            print("Make sure the Unity simulation is running and the server is active.")
            sys.exit(1)
        except Exception as e:
            print(f"[ERROR] Unexpected error connecting to robot: {e}")
            sys.exit(1)

    def _check_for_updates(self):
        try:
            print(f"[{self.__class__.__name__}] Checking for updates...")
            with urllib.request.urlopen(VERSION_CHECK_URL, timeout=3) as response:
                server_version = response.read().decode('utf-8').strip()
            
            if server_version > CURRENT_VERSION:
                print(f"[{self.__class__.__name__}] Update received: {server_version} (Current: {CURRENT_VERSION})")
                self._show_update_dialog(server_version)
            else:
                print(f"[{self.__class__.__name__}] You are using the latest version ({CURRENT_VERSION}).")
                
        except urllib.error.URLError:
            print(f"[WARNING] Could not check for updates. (Network error or invalid URL)")
        except Exception as e:
            print(f"[WARNING] Update check failed: {e}")

    def _show_update_dialog(self, new_version):
        root = tk.Tk()
        root.withdraw() 
        msg = f"New version {new_version} is available!\n(Current: {CURRENT_VERSION})\n\nClick 'Yes' to download and restart automatically."
        result = messagebox.askyesno("Update Required", msg)
        root.destroy()
        if result:
            self._perform_update()

    def _perform_update(self):
        if not getattr(sys, 'frozen', False):
            print("[WARNING] Auto-update is only available when running as a compiled .exe file.")
            print(f"Please manually download the update from: {RELEASE_PAGE_URL}")
            return

        download_url = "https://github.com/Daehyun04225768/ServingBot/releases/latest/download/ServingRobot.exe"
        current_exe_path = sys.executable
        directory = os.path.dirname(current_exe_path)
        new_exe_name = "new_update.exe"
        new_exe_path = os.path.join(directory, new_exe_name)
        bat_path = os.path.join(directory, "updater.bat")

        print(f"[{self.__class__.__name__}] Downloading update...")
        try:
            urllib.request.urlretrieve(download_url, new_exe_path)
            print(f"[{self.__class__.__name__}] Download complete.")
        except Exception as e:
            print(f"[ERROR] Download failed: {e}")
            return

        bat_content = f"""@echo off
timeout /t 1 /nobreak > NUL
del "{current_exe_path}"
ren "{new_exe_path}" "{os.path.basename(current_exe_path)}"
start "" "{current_exe_path}"
del "%~f0"
"""
        try:
            with open(bat_path, "w") as f:
                f.write(bat_content)
            
            print(f"[{self.__class__.__name__}] Restarting to apply updates...")
            subprocess.Popen(f'"{bat_path}"', shell=True)
            sys.exit(0)
            
        except Exception as e:
            print(f"[ERROR] Failed to start update process: {e}")

    def send_command(self, command_str):
        if not self.sock: return None
        try:
            print(f" -> Sending: '{command_str}'")
            self.sock.sendall(command_str.encode('utf-8'))
            time.sleep(0.5)
            self.sock.settimeout(0.1)
            try:
                data = self.sock.recv(1024)
                if data:
                    response = data.decode('utf-8').strip()
                    print(f" <- Received: '{response}'")
                    if "[SYSTEM ALERT]" in response:
                        self._handle_emergency(response)
                    return response
            except socket.timeout:
                pass
            finally:
                self.sock.settimeout(None)

        except socket.error as e:
            print(f"[ERROR] Socket communication error: {e}")
            self.close()
            sys.exit(1)
        return None

    def _handle_emergency(self, msg):
        print("\n" + "!"*40)
        print(f" [EMERGENCY] Robot Stopped due to: {msg}")
        print("!"*40 + "\n")
        self.close()
        raise RuntimeError(f"Robot Collision/Emergency: {msg}")

    def move(self, distance):
        self.send_command(f"Move:{distance}")

    def turn(self, angle):
        self.send_command(f"Turn:{angle}")
    
    def led_on(self):
        self.send_command("LED:1")
        
    def led_off(self):
        self.send_command("LED:0")

    def move_to(self, x, z):
        return self.send_command(f"MoveTo:{x},{z}")

    def scan(self):
        return self.send_command("Scan")

    def get_status(self):
        return self.send_command("Status")

    def charge(self):
        return self.send_command("Charge")

    def close(self):
        if self.sock:
            self.sock.close()
            self.sock = None
            print(f"[{self.__class__.__name__}] Connection closed.")

# =====================================================================
# 🚨 학생 보호용 안전장치 (이 파일을 직접 실행했을 때의 동작)
# =====================================================================
if __name__ == "__main__":
    print("\n" + "="*50)
    print(" ⚠️ 안내: 이 파일은 로봇을 구동하는 '엔진(라이브러리)' 입니다.")
    print(" ⚠️ 실습을 위해서는 'student_mission.py' 파일을 실행해주세요!")
    print("="*50 + "\n")