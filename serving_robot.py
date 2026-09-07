import socket
import threading
import queue
import time
import json
import re
import urllib.request
import tkinter as tk
from tkinter import messagebox
import webbrowser
import sys
import os
import subprocess

# 설정
CURRENT_VERSION = "1.1.0"
VERSION_CHECK_URL = "https://raw.githubusercontent.com/Daehyun04225768/ServingBot/main/version.txt"
RELEASE_PAGE_URL = "https://github.com/Daehyun04225768/ServingBot/releases"

_ACK_PREFIX = "[ROBOT ACK] "
_ALERT_PREFIX = "[SYSTEM ALERT] "
_EVENT_PREFIX = "[EVENT] "
_MSG_SPLIT_RE = re.compile(r"(?=\[ROBOT ACK\]|\[SYSTEM ALERT\]|\[EVENT\])")


class CollisionError(Exception):
    """robot.move_safe() 이동 중 충돌이 발생했을 때 발생합니다."""
    pass


class NotAtStationError(Exception):
    """robot.charge_at_station()을 충전소 반경(1m) 밖에서 호출했을 때 발생합니다."""
    pass


class ServingRobot:
    def __init__(self, agent=1, ip='127.0.0.1', port=5555):
        print(f"[{self.__class__.__name__}] Initializing (agent={agent})...")
        self._check_for_updates()

        self.agent = agent
        self.name = f"robot{agent}"
        self.ip = ip
        self.port = port
        self.sock = None
        self.history = []
        self._is_moving_cache = False

        self._response_queue = queue.Queue()
        self._collision_flag = threading.Event()
        self._last_collision_name = None
        self._on_collision_cb = None
        self._on_obstacle_cb = None
        self._on_battery_low_cb = None
        self._running = False
        self._listener_thread = None

        self.connect()

    # ------------------------------------------------------------------
    # 연결 / 핸드셰이크
    # ------------------------------------------------------------------
    def connect(self):
        """Unity 시뮬레이터에 연결하고 'Connect:<agent>' 핸드셰이크를 보냅니다.
        시뮬레이터가 실행 중이 아니면 ConnectionError가 발생합니다."""
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.connect((self.ip, self.port))
        except (ConnectionRefusedError, OSError) as e:
            self.sock = None
            raise ConnectionError(
                f"Could not connect to Unity server at {self.ip}:{self.port}. "
                f"Make sure the Unity simulation is running. ({e})"
            )

        # v1.1.0: 싱글 윈도우 멀티 에이전트 — 접속 직후 반드시 핸드셰이크를 먼저 보내야 함
        self.sock.sendall(f"Connect:{self.agent}".encode('utf-8'))

        self._running = True
        self._listener_thread = threading.Thread(target=self._listen_loop, daemon=True)
        self._listener_thread.start()

        # 핸드셰이크 응답(Connected as agent N)을 잠깐 기다립니다.
        try:
            self._response_queue.get(timeout=2.0)
            print(f"[{self.__class__.__name__}] Connected to Unity Simulator at "
                  f"{self.ip}:{self.port} as agent {self.agent}")
        except queue.Empty:
            print(f"[WARNING] Handshake acknowledgement not received in time — continuing anyway.")

    def close(self):
        self._running = False
        if self.sock:
            try:
                self.sock.close()
            except OSError:
                pass
            self.sock = None
            print(f"[{self.__class__.__name__}] Connection closed.")

    # ------------------------------------------------------------------
    # 내부 통신 처리 (백그라운드 리스너 스레드)
    # ------------------------------------------------------------------
    def _listen_loop(self):
        buffer = ""
        while self._running and self.sock:
            try:
                data = self.sock.recv(4096)
            except OSError:
                break
            if not data:
                break
            buffer += data.decode('utf-8', errors='replace')

            # 하나의 recv()에 여러 메시지가 함께 도착할 수 있어 알려진 접두어 기준으로 분리합니다.
            parts = [p for p in _MSG_SPLIT_RE.split(buffer) if p]
            if not parts:
                continue
            # 마지막 조각이 잘려있을 수도 있으니(다음 recv에서 이어붙임), 완결된 것만 처리하고
            # 알려진 접두어로 시작하지 않는 마지막 조각은 버퍼에 남겨둡니다.
            buffer = ""
            for part in parts:
                part = part.strip()
                if not part:
                    continue
                if part.startswith(_ACK_PREFIX) or part.startswith(_ALERT_PREFIX) or part.startswith(_EVENT_PREFIX):
                    self._dispatch_message(part)
                else:
                    buffer = part  # 불완전한 조각 — 다음 수신과 이어붙여 재시도

    def _dispatch_message(self, msg):
        if msg.startswith(_ACK_PREFIX):
            self._response_queue.put(msg[len(_ACK_PREFIX):])
        elif msg.startswith(_ALERT_PREFIX):
            body = msg[len(_ALERT_PREFIX):]
            print(f"\n{'!' * 40}\n [SYSTEM ALERT] {body}\n{'!' * 40}\n")
        elif msg.startswith(_EVENT_PREFIX):
            self._handle_event(msg[len(_EVENT_PREFIX):])

    def _handle_event(self, body):
        if body.startswith("COLLISION:"):
            name = body.split(":", 1)[1]
            self._last_collision_name = name
            self._collision_flag.set()
            if self._on_collision_cb:
                threading.Thread(target=self._on_collision_cb, daemon=True).start()
        elif body.startswith("OBSTACLE_NEAR:"):
            dist = body.split(":", 1)[1]
            if self._on_obstacle_cb:
                threading.Thread(target=self._on_obstacle_cb, daemon=True).start()
        elif body.startswith("BATTERY_LOW:"):
            level = body.split(":", 1)[1]
            if self._on_battery_low_cb:
                threading.Thread(target=self._on_battery_low_cb, daemon=True).start()

    def send_command(self, command_str, timeout=2.0):
        """원시 명령 문자열을 직접 전송하고 [ROBOT ACK] 본문을 문자열로 반환합니다."""
        if not self.sock:
            raise ConnectionError("Not connected to the Unity simulator.")
        try:
            print(f" -> Sending: '{command_str}'")
            self.sock.sendall(command_str.encode('utf-8'))
        except socket.error as e:
            self.close()
            raise ConnectionError(f"Socket communication error: {e}")

        try:
            response = self._response_queue.get(timeout=timeout)
            print(f" <- Received: '{response}'")
            return response
        except queue.Empty:
            print(f"[WARNING] No response for '{command_str}' within {timeout}s")
            return ""

    def _raise_if_error(self, response):
        m = re.match(r"\[ERROR:(\w+)\]\s*(.*)", response)
        if m:
            cls_name, message = m.group(1), m.group(2)
            error_types = {"NotAtStationError": NotAtStationError, "CollisionError": CollisionError}
            raise error_types.get(cls_name, RuntimeError)(message)
        return response

    def _log_history(self, action, **kwargs):
        entry = {"action": action, "timestamp": time.time()}
        entry.update(kwargs)
        self.history.append(entry)

    # ------------------------------------------------------------------
    # 기본 이동 / 상태 API (v1.0.0 — 동작 방식 유지)
    # ------------------------------------------------------------------
    def move(self, distance, speed=1.0):
        self._log_history("move", distance=distance, speed=speed)
        if speed != 1.0:
            self.send_command(f"Move:{distance},{speed}")
        else:
            self.send_command(f"Move:{distance}")

    def turn(self, degree):
        self._log_history("turn", degree=degree)
        self.send_command(f"Turn:{degree}")

    def move_to(self, x, z):
        self._log_history("move_to", x=x, z=z)
        self.send_command(f"MoveTo:{x},{z}")

    def scan(self):
        response = self.send_command("Scan")
        return self._parse_scan(response)

    def get_status(self):
        response = self.send_command("Status")
        return self._parse_status(response)

    def charge(self):
        self._log_history("charge")
        self.send_command("Charge")

    def led_on(self):
        self.send_command("LED:1")

    def led_off(self):
        self.send_command("LED:0")

    def set_led(self, on: bool):
        self.send_command(f"LED:{1 if on else 0}")

    # ------------------------------------------------------------------
    # 6주차 — 조건문(if/elif/else)
    # ------------------------------------------------------------------
    def stop(self):
        self.send_command("Stop")

    def wait(self, seconds):
        time.sleep(seconds)

    def move_curve(self, target_x, target_z):
        self._log_history("move_curve", x=target_x, z=target_z)
        self.send_command(f"MoveCurve:{target_x},{target_z}")

    def set_speed(self, ratio):
        self.send_command(f"SetSpeed:{ratio}")

    def is_obstacle_near(self, threshold=1.0):
        response = self.send_command(f"IsObstacleNear:{threshold}")
        return self._parse_bool("[IS_OBSTACLE_NEAR]", response)

    def is_charged(self, min_pct=80):
        response = self.send_command(f"IsCharged:{min_pct}")
        return self._parse_bool("[IS_CHARGED]", response)

    def has_detected(self, name: str):
        response = self.send_command(f"HasDetected:{name}")
        return self._parse_bool("[HAS_DETECTED]", response)

    def get_alert_level(self):
        response = self.send_command("GetAlertLevel")
        return response.replace("[ALERT_LEVEL]", "").strip()

    def get_battery(self):
        response = self.send_command("GetBattery")
        return float(response.replace("[BATTERY]", "").strip())

    # ------------------------------------------------------------------
    # 7주차 — 반복문(for/while) + 리스트
    # ------------------------------------------------------------------
    def move_repeat(self, distance, count):
        self._log_history("move_repeat", distance=distance, count=count)
        self.send_command(f"MoveRepeat:{distance},{count}")

    def patrol(self, waypoints: list):
        self._log_history("patrol", waypoints=waypoints)
        arg = "|".join(f"{x},{z}" for x, z in waypoints)
        self.send_command(f"Patrol:{arg}")

    def sweep(self, start_angle, end_angle, step):
        response = self.send_command(f"Sweep:{start_angle},{end_angle},{step}")
        body = response.replace("[SWEEP]", "").strip()
        if not body:
            return []
        result = []
        for token in body.split("|"):
            angle_str, dist_str = token.split(":")
            result.append((float(angle_str), None if dist_str == "None" else float(dist_str)))
        return result

    def scan_until(self, condition_fn, interval=2):
        while True:
            result = self.scan_lidar()
            if condition_fn(result):
                return result
            time.sleep(interval)

    def get_event_history(self, limit=10):
        response = self.send_command(f"GetEventHistory:{limit}")
        body = response.replace("[EVENT_HISTORY]", "").strip()
        return json.loads(body) if body else []

    def get_log(self):
        response = self.send_command("GetLog")
        body = response.replace("[LOG]", "").strip()
        return json.loads(body) if body else []

    # ------------------------------------------------------------------
    # 9주차 — 함수 정의(def) + 콜백
    # ------------------------------------------------------------------
    def get_position(self):
        response = self.send_command("GetPosition")
        body = response.replace("[POSITION]", "").strip()
        x_str, z_str = body.split(",")
        return (float(x_str), float(z_str))

    def get_direction(self):
        response = self.send_command("GetDirection")
        return response.replace("[DIRECTION]", "").strip()

    def on_collision(self, callback):
        self._on_collision_cb = callback

    def on_obstacle(self, threshold, callback):
        self._on_obstacle_cb = callback
        self.send_command(f"SetObstacleThreshold:{threshold}")

    def on_battery_low(self, pct, callback):
        self._on_battery_low_cb = callback
        self.send_command(f"SetBatteryLowThreshold:{pct}")

    @property
    def is_moving(self):
        response = self.send_command("IsMoving")
        return self._parse_bool("[IS_MOVING]", response)

    # ------------------------------------------------------------------
    # 10주차 — 딕셔너리 + 예외 처리(try/except)
    # ------------------------------------------------------------------
    def get_camera_view(self):
        response = self.send_command("GetCameraView")
        return self._parse_camera_view(response)

    def get_nearby_objects(self):
        response = self.send_command("GetNearbyObjects")
        body = response.replace("[NEARBY]", "").strip()
        if not body or body == "None":
            return []
        result = []
        for token in body.split(";"):
            name, obj_type, coords, dist = token.split("|")
            x_str, z_str = coords.split(",")
            result.append({
                "name": name, "type": obj_type,
                "x": float(x_str), "z": float(z_str), "distance": float(dist),
            })
        return result

    def find_object(self, name: str):
        response = self.send_command(f"FindObject:{name}")
        body = response.replace("[FIND]", "").strip()
        if body == "None" or not body:
            return None
        obj_name, obj_type, coords, dist = body.split("|")
        x_str, z_str = coords.split(",")
        return {
            "name": obj_name, "type": obj_type,
            "x": float(x_str), "z": float(z_str), "distance": float(dist),
        }

    def move_safe(self, distance, poll_interval=0.1, timeout=10.0):
        """이동 중 충돌이 발생하면 CollisionError를 발생시킵니다."""
        self._log_history("move_safe", distance=distance)
        self._collision_flag.clear()
        self.send_command(f"MoveSafe:{distance}")

        deadline = time.time() + timeout
        while time.time() < deadline:
            if self._collision_flag.is_set():
                raise CollisionError(f"Collision with {self._last_collision_name} during move_safe({distance})")
            if not self.is_moving:
                break
            time.sleep(poll_interval)

        if self._collision_flag.is_set():
            raise CollisionError(f"Collision with {self._last_collision_name} during move_safe({distance})")

    def charge_at_station(self):
        response = self.send_command("ChargeAtStation")
        self._raise_if_error(response)

    def display(self, text: str):
        self.send_command(f"Display:{text}")

    def speak(self, text: str):
        self.send_command(f"Speak:{text}")

    def play_sound(self, clip: str):
        self.send_command(f"PlaySound:{clip}")

    # ------------------------------------------------------------------
    # 11주차 — 알고리즘 종합 응용 (동선 최적화)
    # ------------------------------------------------------------------
    def get_explored_map(self):
        response = self.send_command("GetExploredMap")
        return self._parse_explored_map(response)

    def navigate_auto(self, waypoints: list):
        arg = "|".join(f"{x},{z}" for x, z in waypoints)
        response = self.send_command(f"NavigateAuto:{arg}")
        body = response.replace("[NAVIGATE_AUTO]", "").strip().replace("order:", "")
        if not body:
            return []
        return [tuple(map(float, pair.split(","))) for pair in body.split("|")]

    def get_route_distance(self, waypoints: list):
        arg = "|".join(f"{x},{z}" for x, z in waypoints)
        response = self.send_command(f"GetRouteDistance:{arg}")
        return float(response.replace("[ROUTE_DISTANCE]", "").strip())

    # ------------------------------------------------------------------
    # LiDAR / 초음파 센서 (6주차)
    # ------------------------------------------------------------------
    def scan_lidar(self):
        response = self.send_command("ScanLidar")
        return self._parse_lidar(response)

    def scan_ultrasonic(self):
        response = self.send_command("ScanUltrasonic")
        return self._parse_ultrasonic(response)

    # ------------------------------------------------------------------
    # 응답 파서
    # ------------------------------------------------------------------
    @staticmethod
    def _parse_bool(tag, response):
        return response.replace(tag, "").strip() == "true"

    @staticmethod
    def _parse_objects_blob(blob):
        """'name(x:1.0,z:2.0,dist:3.0) name2(...)' 또는 쉼표로 구분된 형태를 리스트[dict]로 변환합니다."""
        if not blob or blob == "None":
            return []
        objs = []
        for m in re.finditer(r"([A-Za-z0-9_]+)\(x:([-\d.]+),z:([-\d.]+),dist:([-\d.]+)\)", blob):
            objs.append({
                "name": m.group(1), "x": float(m.group(2)),
                "z": float(m.group(3)), "distance": float(m.group(4)),
            })
        return objs

    def _parse_scan(self, response):
        body = response.replace("[SCAN]", "").strip()
        obstacle_part, _, objects_part = body.partition("|")
        obstacle_str = obstacle_part.replace("ObstacleDistance:", "").strip()
        objects_str = objects_part.replace("DetectedObjects:", "").strip()
        return {
            "obstacle_distance": None if obstacle_str == "None" else float(obstacle_str),
            "detected_objects": self._parse_objects_blob(objects_str),
        }

    def _parse_status(self, response):
        body = response.replace("[STATUS]", "").strip()
        result = {}
        for field in body.split("|"):
            field = field.strip()
            key, _, value = field.partition(":")
            key = key.strip()
            value = value.strip()
            if key == "Position":
                x_str, z_str = value.strip("()").split(",")
                result["position"] = (float(x_str), float(z_str))
            elif key == "Rotation":
                result["rotation"] = float(value)
            elif key == "Battery":
                result["battery"] = float(value.rstrip("%"))
            elif key == "LED":
                result["led"] = value == "ON"
            elif key == "LastCollision":
                result["last_collision"] = value
        return result

    def _parse_lidar(self, response):
        body = response.replace("[LIDAR]", "").strip()
        sections = body.split("|")
        objects_str = "None"
        dir_tokens = []
        for sec in sections:
            sec = sec.strip()
            if sec.startswith("detected_objects:"):
                objects_str = sec.replace("detected_objects:", "").strip()
            elif sec:
                dir_tokens.append(sec)

        result = {}
        for token in dir_tokens:
            label, _, val = token.partition(":")
            result[label] = None if val == "None" else float(val)
        result["detected_objects"] = self._parse_objects_blob(objects_str)
        return result

    def _parse_ultrasonic(self, response):
        body = response.replace("[ULTRASONIC]", "").strip()
        result = {}
        for token in body.split("|"):
            key, _, value = token.partition(":")
            if key == "distance":
                result["distance"] = None if value == "None" else float(value)
            elif key == "warning":
                result["warning"] = value == "true"
        return result

    def _parse_camera_view(self, response):
        body = response.replace("[CAMERA]", "").strip()
        # 'objects:' 섹션 자체가 '|'를 필드 구분자로 여러 개 포함하므로, 마지막
        # '| obstacle_distance:' 구분자를 기준으로 잘라내야 합니다.
        objects_part, _, obstacle_str = body.rpartition("| obstacle_distance:")
        objects_str = objects_part.replace("objects:", "").strip()
        obstacle_str = obstacle_str.strip()

        camera_view = []
        if objects_str and objects_str != "None":
            for entry in objects_str.split(";"):
                name, obj_type, dist, angle = entry.split("|")
                camera_view.append({
                    "name": name, "type": obj_type,
                    "distance": float(dist), "angle": float(angle),
                })
        return {
            "camera_view": camera_view,
            "obstacle_distance": None if obstacle_str == "None" else float(obstacle_str),
        }

    def _parse_explored_map(self, response):
        body = response.replace("[MAP]", "").strip()
        result = {"visited_cells": [], "obstacle_cells": []}
        for section in body.split("|"):
            section = section.strip()
            if section.startswith("visited:"):
                cells_str = section.replace("visited:", "").strip()
                result["visited_cells"] = self._parse_cells(cells_str)
            elif section.startswith("obstacles:"):
                cells_str = section.replace("obstacles:", "").strip()
                result["obstacle_cells"] = self._parse_cells(cells_str)
            elif section.startswith("cell_size:"):
                result["cell_size"] = float(section.replace("cell_size:", "").strip())

        all_cells = result["visited_cells"] + result["obstacle_cells"]
        if all_cells:
            xs = [c[0] for c in all_cells]
            zs = [c[1] for c in all_cells]
            result["bounds"] = {"x_min": min(xs), "x_max": max(xs), "z_min": min(zs), "z_max": max(zs)}
        else:
            result["bounds"] = {"x_min": 0, "x_max": 0, "z_min": 0, "z_max": 0}
        return result

    @staticmethod
    def _parse_cells(cells_str):
        if not cells_str:
            return []
        cells = []
        for token in cells_str.split(";"):
            if not token:
                continue
            x_str, y_str = token.split(",")
            cells.append((int(x_str), int(y_str)))
        return cells

    # ------------------------------------------------------------------
    # 자동 업데이트 확인 (기존 기능 유지)
    # ------------------------------------------------------------------
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


# =====================================================================
# 🚨 학생 보호용 안전장치 (이 파일을 직접 실행했을 때의 동작)
# =====================================================================
if __name__ == "__main__":
    print("\n" + "=" * 50)
    print(" ⚠️ 안내: 이 파일은 로봇을 구동하는 '엔진(라이브러리)' 입니다.")
    print(" ⚠️ 실습을 위해서는 'student_mission.py' 파일을 실행해주세요!")
    print("=" * 50 + "\n")
