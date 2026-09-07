# ServingBot

AI 서빙 로봇 실습 시뮬레이터 — **스마트로봇 운용과 활용** / **피지컬 AI와 로봇 제어** 과목용 교구입니다.

## 다운로드

학생 실습용 실행 파일(Windows)은 [Releases](../../releases) 페이지에서 `ServingBot.zip`을 받으세요.

## 구성

- `CODEs/` — Unity C# 소스 코드 (RobotController, ProtocolManager, SocketServer 등)
- `serving_robot.py` — 로봇 제어 파이썬 엔진 (수정 금지)
- `student_mission.py` — 학생 실습용 파일
- `학생용실습가이드.md` / `교수용수업가이드.md` — 실습 가이드 문서
- `version.txt` — 현재 버전 (자동 업데이트 체크용)

## 주요 명령어

| 메서드 | 설명 |
| --- | --- |
| `robot.move(distance)` | 전진/후진 |
| `robot.turn(angle)` | 회전 |
| `robot.led_on()` / `led_off()` | LED 제어 |
| `robot.move_to(x, z)` | 좌표로 직접 이동 |
| `robot.scan()` | 장애물 거리 + 주변 인식 오브젝트 조회 |
| `robot.get_status()` | 위치·배터리·LED 상태 조회 |
| `robot.charge()` | 배터리 충전 |

자세한 사용법은 `학생용실습가이드.md`를 참고하세요.
