"""Sends one request to the autopilot server in a running Player and prints the answer.

usage: dexc-remote.py <token file> <port> <operation> [file to send]
"""
import base64
import pathlib
import socket
import sys


def main():
    token = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").strip()
    port = int(sys.argv[2])
    line = token + " " + sys.argv[3]
    if len(sys.argv) > 4:
        line += " " + base64.b64encode(pathlib.Path(sys.argv[4]).read_bytes()).decode("ascii")
    with socket.create_connection(("127.0.0.1", port), timeout=30) as connection:
        connection.sendall((line + "\n").encode("utf-8"))
        with connection.makefile("r", encoding="utf-8") as reader:
            print(reader.readline().strip())


if __name__ == "__main__":
    main()
