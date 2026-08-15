using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class SocketServer : MonoBehaviour
{
    private TcpListener server; 
    private TcpClient client; 
    private Thread serverThread;
    
    public int port = 5555; 
    private bool isRunning = false;

    void Start()
    {
        // 실행 시 "-port=5556" 같은 인자를 주면 해당 포트로 엽니다. (다중 로봇 실습용)
        // 예: ServingRobot.exe -port=5556  → 2호기를 5556 포트로 실행
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("-port="))
            {
                int.TryParse(arg.Substring("-port=".Length), out port);
            }
        }

        // 서버 스레드 시작
        serverThread = new Thread(new ThreadStart(StartServer));
        serverThread.IsBackground = true;
        serverThread.Start();
    }

    void StartServer()
    {
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;
            Debug.Log($"[Unity 서버] 포트 {port} 개방. 접속 대기중...");

            while (isRunning)
            {
                client = server.AcceptTcpClient();
                Debug.Log("✅ 클라이언트 접속 성공!");

                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    
                    // [중요] 중재자(ProtocolManager)에게 데이터 전달
                    if (ProtocolManager.Instance != null)
                    {
                        ProtocolManager.Instance.OnReceiveCommand(data);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.Log("❌ 서버 에러: " + e.Message);
        }
    }

    // ProtocolManager가 호출하여 파이썬으로 메시지를 보낼 때 사용
    public void SendMessageToClient(string message)
    {
        if (client != null && client.Connected)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] msg = Encoding.UTF8.GetBytes(message);
                stream.Write(msg, 0, msg.Length);
            }
            catch (System.Exception e)
            {
                Debug.Log("❌ 송신 에러: " + e.Message);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (server != null) server.Stop();
        if (serverThread != null) serverThread.Abort();
    }
}