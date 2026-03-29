"""
Quick diagnostic: start mock server, log all connections and incoming messages.
Run this, then enter Unity Play Mode, and watch the output.

Usage: uv run python tests/test_connection_debug.py
"""

import asyncio
import logging
from mock_server.server import MockMCPServer

logging.basicConfig(level=logging.DEBUG, format="%(asctime)s %(levelname)s %(name)s: %(message)s")


async def main():
    srv = MockMCPServer()
    await srv.start()
    print(f"\n=== Mock Server listening on {srv.url} ===")
    print("Waiting for Unity to connect... (enter Play Mode now)")
    print("Press Ctrl+C to stop.\n")

    try:
        while True:
            if srv._connections:
                print(f"[CONNECTED] {len(srv._connections)} client(s) connected!")
                # Wait a bit then try sending a ping
                await asyncio.sleep(1)
                try:
                    req_id = await srv.send_request("get_inventory", {})
                    print(f"[SENT] get_inventory request id={req_id}")
                    response = await srv.wait_for_response(req_id, timeout=5.0)
                    print(f"[RECEIVED] Response: {response}")
                except asyncio.TimeoutError:
                    print("[TIMEOUT] No response from Unity within 5s")
                except Exception as e:
                    print(f"[ERROR] {e}")
                break
            await asyncio.sleep(0.5)
    except KeyboardInterrupt:
        pass
    finally:
        await srv.stop()
        print("\nServer stopped.")


if __name__ == "__main__":
    asyncio.run(main())
