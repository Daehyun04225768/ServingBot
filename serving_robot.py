import socket
import threading
import queue
import time
import json
import re
import urllib.request

# 설정
CURRENT_VERSION = "1.1.2"
VERSION_CHECK_URL = "https://raw.githubusercontent.com/Daehyun04225768/ServingBot/main/version.txt"


# =====================================================================
# 예외 클래스 (10주차 try/except 학습용)
# =====================================================================
class CollisionError(Exception):
    """move_safe() 이동 중 충돌이 발생했을 때 발생합니다."""
    pass


class NotAtStationError(Exception):
    """충전소 반경 1m 밖에서 charge_at_station()을 호출했을 때 발생합니다."""
    pass


class RotationLockedError(Exception):
    """[회전 잠금]이 켜진 상태에서 turn()을 호출했을 때 발생합니다. (6주차)"""
    pass


def _to_float_or_none(s):
    return None if s is None or s == "None" else float(s)


def _to_int_or_none(s):
    return None if s is None or s == "None" else int(s)


def _split_detected_objects(s):
    # "RedBall(x:0.0,z:5.0,dist:7.3),ChargingStation(x:0.0,z:0.0,dist:2.2)" 형태.
    # 괄호 안에 콤마가 들어있어 단순 split(",")로는 잘못 쪼개지므로 "이름(...)" 단위로 정규식 추출한다.
    if s is None or s == "None" or s == "":
        return []
    return re.findall(r"[^,]+\([^)]*\)", s)


class ServingRobot:
    """
    v1.1.0 — 싱글 윈도우 멀티 에이전트 지원 클라이언트.

    싱글 모드(스마트로봇 운용과 활용):  robot = ServingRobot()
    듀얼 모드(피지컬 AI와 로봇 제어):   robot1 = ServingRobot(agent=1)
                                       robot2 = ServingRobot(agent=2)

    이 파일은 "교수님이 만든 로봇 엔진"입니다 — 학생은 student_mission.py만 수정하세요.
    """

    def __init__(self, agent=1, ip="127.0.0.1", port=5555):
        self.agent = agent
        self.name = f"robot{agent}"
        self.ip = ip
        self.port = port
        self.history = []          # 9주차: 이동 이력 (읽기 전용으로 취급)

        self._sock = None
        self._file = None
        self._send_lock = threading.RLock()
        self._response_queue = queue.Queue()
        self._reader_thread = None
        self._running = False

        # 콜백(9주차: on_collision / on_obstacle / on_battery_low)
        self._on_collision = None
        self._on_obstacle = None
        self._on_battery_low = None

        # move_safe()가 소비하기 전까지 남아있는 "처리 안 된" 충돌 경고.
        # move_safe() 밖에서(=일반 move() 등에서) 충돌이 나면, 다음 명령을 보낼 때
        # v1.0.0 영상 속 동작(_handle_emergency)과 같은 방식으로 크래시시킨다.
        self._unhandled_alert = None
        self._move_safe_armed = False
        self._move_safe_alert = None

        # patrol()/move_repeat()/navigate_auto()는 서버가 이동을 여러 개 내부 큐에 쌓아두고
        # 각 이동이 끝날 때마다 별도로 "이동 명령 접수" 응답을 비동기로 보낸다. 이 응답들은
        # 그 시점에 클라이언트가 기다리고 있는 어떤 send_command()의 응답도 아니므로, 지금
        # 실제로 응답을 기다리는 중이 아니면 조용히 버려야 한다 — 그렇지 않으면 이 여분의
        # 응답이 그다음에 보낸 전혀 다른 명령의 응답인 것처럼 잘못 소비된다.
        self._awaiting_response = False

        self._check_for_updates()
        self._connect()

    # -----------------------------------------------------------------
    # 연결 / 저수준 통신
    # -----------------------------------------------------------------
    def _check_for_updates(self):
        try:
            with urllib.request.urlopen(VERSION_CHECK_URL, timeout=3) as response:
                server_version = response.read().decode("utf-8").strip()
            if server_version > CURRENT_VERSION:
                print(f"[{self.__class__.__name__}] 새 버전 {server_version}이(가) 있습니다 (현재: {CURRENT_VERSION}).")
        except Exception:
            pass  # 교실에 인터넷이 없어도 실습에 지장이 없도록 조용히 넘어간다.

    def _connect(self):
        print(f"[{self.__class__.__name__}] Initializing (agent={self.agent})...")
        try:
            self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self._sock.connect((self.ip, self.port))
        except OSError as e:
            # ConnectionRefusedError는 ConnectionError의 서브클래스이므로 그대로 통일해서 던진다.
            raise ConnectionError(
                f"Unity 시뮬레이터({self.ip}:{self.port})에 연결할 수 없습니다. "
                f"시뮬레이터가 켜져 있는지 확인하세요. (원인: {e})"
            ) from e

        self._file = self._sock.makefile("r", encoding="utf-8", newline="\n")
        self._running = True
        self._reader_thread = threading.Thread(target=self._reader_loop, daemon=True)
        self._reader_thread.start()

        # 핸드셰이크: "Connect:<agentId>" — 반드시 첫 메시지여야 한다(SocketServer.cs 참고)
        ack = self._send_and_wait(f"Connect:{self.agent}", is_handshake=True)
        print(f"[{self.__class__.__name__}] {ack}")

    def _reader_loop(self):
        try:
            for raw_line in self._file:
                line = raw_line.rstrip("\r\n")
                if line == "":
                    continue
                self._dispatch_incoming(line)
        except (OSError, ValueError):
            pass  # 소켓이 close()로 정상 종료된 경우 여기로 빠진다.

    def _dispatch_incoming(self, line):
        if line.startswith("[EVENT] "):
            self._handle_event(line[len("[EVENT] "):])
        elif line.startswith("[SYSTEM ALERT] "):
            if self._move_safe_armed:
                self._move_safe_alert = line
            elif self._on_collision is not None:
                # on_collision(callback)이 등록돼 있으면 이 충돌은 콜백이 처리할 몫이다.
                # _unhandled_alert로 남겨두면, 콜백 스레드 안에서 robot.stop() 등을 호출하는
                # 순간 "처리 안 된 충돌"로 다시 감지돼 콜백 자신이 크래시난다 — 그래서 비워둔다.
                pass
            else:
                self._unhandled_alert = line
        elif self._awaiting_response:
            self._response_queue.put(line)
        # else: 아무도 기다리지 않는 응답 — 위 주석 참고, 조용히 버린다.

    def _handle_event(self, payload):
        # 콜백은 반드시 별도 스레드에서 실행한다 — 콜백 안에서 robot.move() 등을
        # 호출하면 그 자체가 새 요청/응답 왕복이라, 리더 스레드 안에서 직접 실행하면
        # 응답을 리더 스레드 자신이 기다리게 되어 데드락이 난다.
        if payload.startswith("COLLISION:") and self._on_collision is not None:
            threading.Thread(target=self._on_collision, daemon=True).start()
        elif payload.startswith("OBSTACLE_NEAR:") and self._on_obstacle is not None:
            threading.Thread(target=self._on_obstacle, daemon=True).start()
        elif payload.startswith("BATTERY_LOW:") and self._on_battery_low is not None:
            threading.Thread(target=self._on_battery_low, daemon=True).start()

    def _handle_emergency(self, msg):
        print("\n" + "!" * 40)
        print(f" [EMERGENCY] Robot Stopped due to: {msg}")
        print("!" * 40 + "\n")
        self.close()
        raise RuntimeError(f"Robot Collision/Emergency: {msg}")

    def send_command(self, command_str, timeout=10.0):
        """원시 명령 문자열을 직접 전송한다 (v1.0.0 API — 하위 호환 유지)."""
        return self._send_and_wait(command_str, timeout=timeout)

    def _send_and_wait(self, command_str, timeout=10.0, is_handshake=False):
        if not is_handshake and self._unhandled_alert is not None:
            alert, self._unhandled_alert = self._unhandled_alert, None
            self._handle_emergency(alert)

        if not self._sock:
            return None

        with self._send_lock:
            # 응답 큐에 이 명령보다 먼저 도착한 응답이 섞여 있을 일은 없다 —
            # _send_lock이 요청-응답 왕복 전체를 감싸므로 한 번에 하나의 명령만 처리된다.
            self._awaiting_response = True
            try:
                # 대기 플래그를 세우기 직전까지 쌓였을 수 있는 이전 명령의 여분 응답을 비운다.
                while not self._response_queue.empty():
                    self._response_queue.get_nowait()
                self._sock.sendall((command_str + "\n").encode("utf-8"))
                try:
                    return self._response_queue.get(timeout=timeout)
                except queue.Empty:
                    print(f"[{self.__class__.__name__}] [WARNING] '{command_str}' 응답 시간 초과.")
                    return None
            finally:
                self._awaiting_response = False

    @staticmethod
    def _unwrap(raw):
        if raw is None:
            return ""
        return raw[len("[ROBOT ACK] "):] if raw.startswith("[ROBOT ACK] ") else raw

    def close(self):
        self._running = False
        if self._sock:
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None
            print(f"[{self.__class__.__name__}] Connection closed.")

    # -----------------------------------------------------------------
    # v1.0.0 API — 1~5주차 촬영 완료분, 반환 형식(prefix 포함 원문 문자열) 그대로 유지
    # -----------------------------------------------------------------
    def move(self, distance, speed=None):
        cmd = f"Move:{distance}" if speed is None else f"Move:{distance},{speed}"
        self.history.append(("move", distance))
        self.send_command(cmd)

    def turn(self, degree):
        resp = self.send_command(f"Turn:{degree}")
        if resp is not None and '"error": "ROTATION_LOCKED"' in resp:
            raise RotationLockedError("회전 잠금이 켜져 있어 turn()을 실행할 수 없습니다.")
        self.history.append(("turn", degree))

    def move_to(self, x, z):
        self.history.append(("move_to", x, z))
        return self.send_command(f"MoveTo:{x},{z}")

    def scan(self):
        return self.send_command("Scan")

    def get_status(self):
        return self.send_command("Status")

    def charge(self):
        return self.send_command("Charge")

    def led_on(self):
        self.send_command("LED:1")

    def led_off(self):
        self.send_command("LED:0")

    def set_led(self, state: bool):
        self.led_on() if state else self.led_off()

    # -----------------------------------------------------------------
    # 6주차 — 조건문 (if / elif / else)
    # -----------------------------------------------------------------
    def stop(self):
        self.send_command("Stop")

    def wait(self, seconds):
        # 서버 왕복이 필요 없는 순수 클라이언트 대기 — time.sleep 그대로 노출.
        time.sleep(seconds)

    def move_curve(self, target_x, target_z):
        self.history.append(("move_curve", target_x, target_z))
        self.send_command(f"MoveCurve:{target_x},{target_z}")

    def set_speed(self, ratio):
        self.send_command(f"SetSpeed:{ratio}")

    def is_obstacle_near(self, threshold=1.0):
        resp = self._unwrap(self.send_command(f"IsObstacleNear:{threshold}"))
        return resp.replace("[IS_OBSTACLE_NEAR] ", "").strip() == "true"

    def is_charged(self, min_pct=80):
        resp = self._unwrap(self.send_command(f"IsCharged:{min_pct}"))
        return resp.replace("[IS_CHARGED] ", "").strip() == "true"

    def has_detected(self, name: str):
        resp = self._unwrap(self.send_command(f"HasDetected:{name}"))
        return resp.replace("[HAS_DETECTED] ", "").strip() == "true"

    def get_alert_level(self):
        resp = self._unwrap(self.send_command("GetAlertLevel"))
        return resp.replace("[ALERT_LEVEL] ", "").strip()

    def get_battery(self):
        resp = self._unwrap(self.send_command("GetBattery"))
        return float(resp.replace("[BATTERY] ", "").strip())

    # -----------------------------------------------------------------
    # 7주차 — 반복문 (for / while) + 리스트
    # -----------------------------------------------------------------
    def move_repeat(self, distance, count):
        self.send_command(f"MoveRepeat:{distance},{count}")

    def patrol(self, waypoints: list):
        arg = "|".join(f"{x},{z}" for x, z in waypoints)
        self.send_command(f"Patrol:{arg}")

    def sweep(self, start_angle, end_angle, step):
        resp = self._unwrap(self.send_command(f"Sweep:{start_angle},{end_angle},{step}"))
        body = resp.replace("[SWEEP] ", "").strip()
        if not body:
            return []
        readings = []
        for token in body.split("|"):
            angle_str, dist_str = token.split(":", 1)
            readings.append({"angle": float(angle_str), "distance": _to_float_or_none(dist_str)})
        return readings

    def scan_until(self, condition_fn, interval=2):
        # while 루프 패턴 체험용 — 서버에 새 명령을 추가하지 않고 scan_lidar()를 반복 호출한다.
        while True:
            result = self.scan_lidar()
            if condition_fn(result):
                return result
            time.sleep(interval)

    def get_event_history(self, limit=10):
        resp = self._unwrap(self.send_command(f"GetEventHistory:{limit}"))
        body = resp.replace("[EVENT_HISTORY] ", "", 1)
        return json.loads(body)

    def get_log(self):
        resp = self._unwrap(self.send_command("GetLog"))
        body = resp.replace("[LOG] ", "", 1)
        return json.loads(body)

    # -----------------------------------------------------------------
    # 9주차 — 함수 정의(def) + 콜백 + 튜플
    # -----------------------------------------------------------------
    def get_position(self):
        resp = self._unwrap(self.send_command("GetPosition"))
        x_str, z_str = resp.replace("[POSITION] ", "").strip().split(",")
        return (float(x_str), float(z_str))

    def get_direction(self):
        resp = self._unwrap(self.send_command("GetDirection"))
        return resp.replace("[DIRECTION] ", "").strip()

    def on_collision(self, callback):
        self._on_collision = callback

    def on_obstacle(self, threshold, callback):
        self._on_obstacle = callback
        self.send_command(f"SetObstacleThreshold:{threshold}")

    def on_battery_low(self, pct, callback):
        self._on_battery_low = callback
        self.send_command(f"SetBatteryLowThreshold:{pct}")

    @property
    def is_moving(self):
        resp = self._unwrap(self.send_command("IsMoving"))
        return resp.replace("[IS_MOVING] ", "").strip() == "true"

    # -----------------------------------------------------------------
    # 6주차(심화) / 10주차 — 센서 딕셔너리 + 예외 처리
    # -----------------------------------------------------------------
    def scan_lidar(self):
        resp = self._unwrap(self.send_command("ScanLidar"))
        body = resp.replace("[LIDAR] ", "", 1)
        readings_part, objects_part = body.split(" | detected_objects:", 1)
        result = {}
        for token in readings_part.split("|"):
            key, val = token.split(":", 1)
            result[key] = _to_float_or_none(val)
        result["detected_objects"] = _split_detected_objects(objects_part.strip())
        return result

    def scan_ultrasonic(self):
        resp = self._unwrap(self.send_command("ScanUltrasonic"))
        body = resp.replace("[ULTRASONIC] ", "", 1)
        dist_part, warn_part = body.split("|")
        distance = _to_float_or_none(dist_part.split(":", 1)[1])
        warning = warn_part.split(":", 1)[1].strip() == "true"
        return {"distance": distance, "warning": warning}

    def get_camera_view(self):
        resp = self._unwrap(self.send_command("GetCameraView"))
        body = resp.replace("[CAMERA] ", "", 1)
        objects_part, obstacle_part = body.split(" | obstacle_distance:", 1)
        objects_str = objects_part.replace("objects:", "", 1).strip()

        camera_view = []
        if objects_str != "None":
            for entry in objects_str.split(";"):
                name, obj_type, dist, angle = entry.split("|")
                camera_view.append({
                    "name": name,
                    "type": obj_type,
                    "distance": float(dist),
                    "angle": float(angle),
                })
        return {
            "camera_view": camera_view,
            "obstacle_distance": _to_float_or_none(obstacle_part.strip()),
        }

    def get_nearby_objects(self):
        resp = self._unwrap(self.send_command("GetNearbyObjects"))
        body = resp.replace("[NEARBY] ", "", 1).strip()
        if body == "None":
            return []
        result = []
        for entry in body.split(";"):
            name, obj_type, xz, dist = entry.split("|")
            x_str, z_str = xz.split(",")
            result.append({
                "name": name, "type": obj_type,
                "x": float(x_str), "z": float(z_str),
                "distance": float(dist),
            })
        return result

    def find_object(self, name: str):
        resp = self._unwrap(self.send_command(f"FindObject:{name}"))
        body = resp.replace("[FIND] ", "", 1).strip()
        if body == "None":
            return None
        found_name, obj_type, xz, dist = body.split("|")
        x_str, z_str = xz.split(",")
        return {
            "name": found_name, "type": obj_type,
            "x": float(x_str), "z": float(z_str),
            "distance": float(dist),
        }

    def move_safe(self, distance):
        self._move_safe_armed = True
        self._move_safe_alert = None
        try:
            self.history.append(("move_safe", distance))
            self.send_command(f"MoveSafe:{distance}")
            # 이동이 실제로 끝나거나(정상) 충돌 경고가 도착할 때까지(비정상) 폴링한다.
            while self.is_moving:
                if self._move_safe_alert is not None:
                    raise CollisionError(self._move_safe_alert)
                time.sleep(0.05)
            if self._move_safe_alert is not None:
                raise CollisionError(self._move_safe_alert)
        finally:
            self._move_safe_armed = False
            self._move_safe_alert = None

    def charge_at_station(self):
        resp = self._unwrap(self.send_command("ChargeAtStation"))
        if resp.startswith("[ERROR:NotAtStationError]"):
            raise NotAtStationError(resp.replace("[ERROR:NotAtStationError] ", ""))
        return resp

    def display(self, text: str):
        text = str(text).replace("\n", " ").replace("\r", " ")
        self.send_command(f"Display:{text}")

    def speak(self, text: str):
        text = str(text).replace("\n", " ").replace("\r", " ")
        self.send_command(f"Speak:{text}")

    def play_sound(self, clip: str):
        self.send_command(f"PlaySound:{clip}")

    # -----------------------------------------------------------------
    # 11주차 — SLAM 맵 + 동선 최적화 내비게이션
    # -----------------------------------------------------------------
    @staticmethod
    def _encode_waypoints(waypoints):
        return "|".join(f"{x},{z}" for x, z in waypoints)

    def get_explored_map(self):
        resp = self._unwrap(self.send_command("GetExploredMap"))
        body = resp.replace("[MAP] ", "", 1)
        visited_part, obstacles_part, cellsize_part = body.split(" | ")

        def parse_cells(part, prefix):
            raw = part.replace(prefix, "", 1).strip()
            if not raw:
                return []
            cells = []
            for token in raw.split(";"):
                x_str, y_str = token.split(",")
                cells.append((int(x_str), int(y_str)))
            return cells

        visited_cells = parse_cells(visited_part, "visited:")
        obstacle_cells = parse_cells(obstacles_part, "obstacles:")
        cell_size = float(cellsize_part.replace("cell_size:", "", 1).strip())

        # 참고: Unity 서버는 "bounds"를 직접 보내지 않는다 — 문서(앱_업그레이드_내역.md)가
        # 약속한 반환 모양을 맞추기 위해 방문 좌표로부터 클라이언트에서 계산한다.
        if visited_cells:
            xs = [c[0] for c in visited_cells]
            ys = [c[1] for c in visited_cells]
            bounds = {"x_min": min(xs), "x_max": max(xs), "z_min": min(ys), "z_max": max(ys)}
        else:
            bounds = {"x_min": 0, "x_max": 0, "z_min": 0, "z_max": 0}

        return {
            "visited_cells": visited_cells,
            "obstacle_cells": obstacle_cells,
            "cell_size": cell_size,
            "bounds": bounds,
        }

    def get_route_distance(self, waypoints: list):
        resp = self._unwrap(self.send_command(f"GetRouteDistance:{self._encode_waypoints(waypoints)}"))
        return float(resp.replace("[ROUTE_DISTANCE] ", "").strip())

    def navigate_auto(self, waypoints: list):
        resp = self._unwrap(self.send_command(f"NavigateAuto:{self._encode_waypoints(waypoints)}"))
        body = resp.replace("[NAVIGATE_AUTO] order:", "", 1).strip()
        if not body:
            return []
        order = []
        for token in body.split("|"):
            x_str, z_str = token.split(",")
            order.append((float(x_str), float(z_str)))
        return order


# =====================================================================
# 🚨 학생 보호용 안전장치 (이 파일을 직접 실행했을 때의 동작)
# =====================================================================
if __name__ == "__main__":
    print("\n" + "=" * 50)
    print(" ⚠️ 안내: 이 파일은 로봇을 구동하는 '엔진(라이브러리)' 입니다.")
    print(" ⚠️ 실습을 위해서는 'student_mission.py' 파일을 실행해주세요!")
    print("=" * 50 + "\n")
