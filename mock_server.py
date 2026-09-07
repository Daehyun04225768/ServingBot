import socket
import sys

def run_mock_server(port=5555):
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    
    try:
        server.bind(('127.0.0.1', port))
    except OSError:
        print(f"Port {port} is already in use. Maybe a previous server is running?")
        return

    server.listen(1)
    print(f"[MockServer] Listening on 127.0.0.1:{port}...")
    
    try:
        conn, addr = server.accept()
        print(f"[MockServer] Connection from {addr}")
        
        while True:
            data = conn.recv(1024)
            if not data:
                break
            
            command = data.decode('utf-8')
            print(f"[MockServer] Received: {command}")
            
            # Simulate logic
            if "Move:999" in command:
                # Secret trigger to test collision
                print("[MockServer] Triggering Collision Alert!")
                conn.sendall(b"[SYSTEM ALERT] Collision Detected")
            else:
                # Normal behavior: might send "Done" or nothing. 
                # Let's send nothing for normal cases as typical simple simulators,
                # or send proper ACK if required. The client code handles 'timeout' gracefully.
                pass 
                
    except KeyboardInterrupt:
        print("\n[MockServer] Stopping...")
    finally:
        server.close()

if __name__ == "__main__":
    run_mock_server()
