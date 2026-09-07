# ==============================================================
# 🎓 AI 서빙 로봇 조종하기 (학생 실습용 파일)
# ==============================================================

# 1. 교수님이 만든 로봇 엔진을 불러옵니다. (수정 금지)
from serving_robot import ServingRobot
import time

# 2. 로봇의 전원을 켜고 연결합니다. (Unity 시뮬레이터가 켜져 있어야 합니다)
robot = ServingRobot()
print("\n=== 🚀 로봇 조종 실습을 시작합니다! ===\n")

try:
    # ----------------------------------------------------------
    # 👇 여기서부터 자유롭게 명령을 내려보세요!
    # ----------------------------------------------------------
 

    
    # [상황] 식당 관리자가 "우리 식당 타일 한 칸은 40cm²야"라고 면적만 알려준 상황
    target_tile_area = 27

    # 1. 거듭제곱 연산자(** 0.5)를 사용하여 타일 한 변의 길이를 구합니다. (루트 계산)
    # 면적의 0.5제곱은 곧 한 변의 길이(Side Length)가 됩니다.
    one_tile_length = target_tile_area ** 0.5 

    # 2. 로봇이 몇 칸을 이동할지 결정합니다.
    tiles_to_move_x = 2 # 가로로 2칸
    tiles_to_move_z = 2 # 세로로 4칸

    # 3. 계산된 '한 변의 길이'를 사용하여 실제 이동 거리를 산출합니다.
    distance_x = one_tile_length * tiles_to_move_x
    distance_z = one_tile_length * tiles_to_move_z

    print(f"--- 거듭제곱을 활용한 타일 규격 역산 ---")
    print(f"타일 면적 {target_tile_area}cm²로부터 계산된 한 변의 길이: {one_tile_length}cm")

    # 4. 실질적인 격자 주행 (직각 주행)
    print(f"\n[가로 주행] {distance_x}cm 이동합니다.")
    robot.move(distance_x) 

    robot.turn(90) # 모퉁이 회전

    print(f"[세로 주행] {distance_z}cm 이동하여 테이블에 도착합니다.")
    robot.move(distance_z)

    
    
    # ----------------------------------------------------------

except Exception as e:
    print(f"\n[오류 발생] 로봇 조종 중 문제가 발생했습니다: {e}")

finally:
    # 3. 실습이 끝나면 안전하게 로봇과의 연결을 종료합니다.
    robot.close()
    print("\n=== 🏁 실습이 종료되었습니다. ===")