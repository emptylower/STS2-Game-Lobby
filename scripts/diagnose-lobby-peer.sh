#!/usr/bin/env bash
# diagnose-lobby-peer.sh
#
# Read-only diagnostic for the "[peer] disabled (set PEER_SELF_ADDRESS to enable)"
# symptom on lobby-service bare-metal / systemd deployments.
#
# Usage (on the target server):
#   sudo bash diagnose-lobby-peer.sh
#   sudo bash diagnose-lobby-peer.sh > peer-diag.txt 2>&1
#
# Touches nothing. Safe to run on a live production lobby-service.

LC_ALL=C
export LC_ALL

hr() { printf '\n===== %s =====\n' "$*"; }

# ---------- 0. Context ----------
hr "0. 环境"
echo "host:      $(hostname)"
echo "kernel:    $(uname -r)"
echo "user:      $(id -un) (uid=$(id -u))"
echo "time:      $(date -Is 2>/dev/null || date)"
echo "systemd:   $(command -v systemctl >/dev/null 2>&1 && echo yes || echo no)"

# ---------- 1. Service detection ----------
hr "1. 候选 systemd 服务"
MATCHED_UNITS=$(systemctl list-units --type=service --all --no-pager 2>/dev/null \
  | awk '{print $1}' | grep -iE 'lobby|sts2|peer' | grep -v '^$')
if [ -n "$MATCHED_UNITS" ]; then
  echo "$MATCHED_UNITS"
else
  echo "(没找到匹配 lobby/sts2/peer 的 systemd 服务)"
fi
PRIMARY_UNIT=$(echo "$MATCHED_UNITS" | head -1)
[ -z "$PRIMARY_UNIT" ] && PRIMARY_UNIT="sts2-lobby.service"
echo
echo "主候选: $PRIMARY_UNIT"

# ---------- 2. Node processes ----------
hr "2. 当前 node 进程"
ps -eo pid,user,etime,cmd 2>/dev/null | grep -iE 'node|dist/server\.js' | grep -v grep \
  || echo "(没看到任何 node 进程)"
NODE_PIDS=$(pgrep -f 'dist/server\.js' 2>/dev/null)
echo
echo "dist/server.js PID 列表: ${NODE_PIDS:-(空)}"

# ---------- 3. Service unit details ----------
hr "3. 服务 unit 内容 ($PRIMARY_UNIT)"
systemctl cat "$PRIMARY_UNIT" 2>/dev/null || echo "(unit 不存在)"
echo
echo "--- runtime 属性 ---"
systemctl show "$PRIMARY_UNIT" \
  -p EnvironmentFiles -p Environment -p ExecStart \
  -p WorkingDirectory -p MainPID -p ActiveState -p SubState \
  -p FragmentPath 2>/dev/null

# ---------- 4. EnvironmentFile content ----------
hr "4. unit 引用的 EnvironmentFile 内容"
ENV_FILES_RAW=$(systemctl show "$PRIMARY_UNIT" -p EnvironmentFiles 2>/dev/null \
  | sed 's/^EnvironmentFiles=//')
ENV_FILE_PATHS=$(echo "$ENV_FILES_RAW" | grep -oE '(/[^ ]+)')

if [ -z "$ENV_FILE_PATHS" ]; then
  echo "(unit 没有 EnvironmentFile 指令；env 来自 ExecStart 自身 / Environment= / 父进程)"
else
  for path in $ENV_FILE_PATHS; do
    echo "--- $path ---"
    if [ ! -f "$path" ]; then
      echo "(文件不存在！这就是问题源头之一)"
      continue
    fi
    ls -la "$path"
    echo
    echo "[peer/host/port 段]"
    grep -nE '^(PEER_|HOST=|PORT=|PUBLIC_ROOM_LIST_ENABLED|SERVER_REGISTRY_BASE_URL)' "$path" \
      || echo "(无相关行)"
    echo
    echo "[行尾 / 字符编码异常检测]"
    if file "$path" | grep -qi 'CRLF'; then
      echo "⚠ 文件是 CRLF 行尾，systemd EnvironmentFile 不解析 Windows 行尾。"
    fi
    if head -c 3 "$path" | od -An -c | grep -q '357 273 277'; then
      echo "⚠ 文件开头有 UTF-8 BOM，systemd 第一行会失效。"
    fi
    if grep -qE '^\s*PEER_SELF_ADDRESS\s*=\s*["'\''].*["'\'']\s*$' "$path"; then
      echo "ℹ PEER_SELF_ADDRESS 用了引号包裹（systemd 接受，但 shell source 时含引号；本服务用 systemd 没问题）。"
    fi
    if grep -qE '^\s*export\s+PEER_' "$path"; then
      echo "⚠ 出现了 export 关键字。systemd EnvironmentFile 不支持 export 前缀，那一行会被忽略。"
    fi
    echo
  done
fi

# ---------- 5. Live process env ----------
hr "5. node 进程实际加载的关键 env"
if [ -z "$NODE_PIDS" ]; then
  echo "(没有跑着的 dist/server.js 进程)"
else
  for pid in $NODE_PIDS; do
    echo "--- PID $pid ---"
    if [ ! -r "/proc/$pid/environ" ]; then
      echo "(无权读 /proc/$pid/environ，请以 root / sudo 执行本脚本)"
      continue
    fi
    echo "cwd=$(readlink "/proc/$pid/cwd")"
    echo "exe=$(readlink "/proc/$pid/exe")"
    echo "cmdline=$(tr '\0' ' ' < /proc/$pid/cmdline)"
    echo
    echo "[关键 env 字段]"
    tr '\0' '\n' < "/proc/$pid/environ" \
      | grep -E '^(PEER_|HOST=|PORT=|SERVER_REGISTRY_BASE_URL=|PUBLIC_ROOM_LIST_ENABLED=|NODE_ENV=)' \
      | sort \
      || echo "(没有任何 PEER_/HOST/PORT 字段，env 完全没注入)"
    echo
  done
fi

# ---------- 6. Common deploy location fallback ----------
hr "6. 兜底：常见部署路径下的 .env"
for candidate in \
  /opt/sts2-lobby/lobby-service/.env \
  /opt/sts2-server-stack-docker/lobby-service/.env \
  /opt/sts2_server_stack_docker/lobby-service/.env \
  /www/wwwroot/sts2-lobby/.env \
  /www/wwwroot/lobby-service/.env \
  /www/server/sts2-lobby/.env \
  /root/sts2-lobby/.env \
  /root/lobby-service/.env \
  /home/sts2/lobby-service/.env; do
  [ -f "$candidate" ] || continue
  echo "--- $candidate ---"
  ls -la "$candidate"
  grep -nE '^(PEER_|HOST=|PORT=|PUBLIC_ROOM_LIST_ENABLED|SERVER_REGISTRY_BASE_URL)' "$candidate" \
    || echo "(无相关行)"
  echo
done

# ---------- 7. Recent peer log lines ----------
hr "7. 最近 30 分钟 peer 相关日志（来自 $PRIMARY_UNIT）"
PEER_LINES=$(journalctl -u "$PRIMARY_UNIT" --since '30 min ago' --no-pager 2>/dev/null \
  | grep -iE 'peer|disabled|mounted|announced|bootstrap|registry')
if [ -n "$PEER_LINES" ]; then
  echo "$PEER_LINES" | tail -60
else
  echo "(没匹配到 peer 行；下面是最近 60 行原始日志)"
  journalctl -u "$PRIMARY_UNIT" --since '30 min ago' --no-pager 2>/dev/null | tail -60
fi

# ---------- 8. Auto verdict ----------
hr "8. 自动判定"

HAS_PEER_IN_PROCESS=0
for pid in $NODE_PIDS; do
  [ -r "/proc/$pid/environ" ] || continue
  if tr '\0' '\n' < "/proc/$pid/environ" | grep -q '^PEER_SELF_ADDRESS='; then
    HAS_PEER_IN_PROCESS=$((HAS_PEER_IN_PROCESS + 1))
  fi
done

HAS_PEER_IN_FILE=0
HAS_PEER_DISABLED_FALSE=0
for path in $ENV_FILE_PATHS; do
  [ -f "$path" ] || continue
  if grep -qE '^PEER_SELF_ADDRESS=' "$path"; then
    HAS_PEER_IN_FILE=1
  fi
  if grep -qE '^PEER_NETWORK_ENABLED=false' "$path"; then
    HAS_PEER_DISABLED_FALSE=1
  fi
done

echo "[计数]"
echo "  进程 env 里出现 PEER_SELF_ADDRESS 的 PID 数:   $HAS_PEER_IN_PROCESS"
echo "  unit 引用的 .env 里出现 PEER_SELF_ADDRESS:    $HAS_PEER_IN_FILE"
echo "  unit 引用的 .env 里 PEER_NETWORK_ENABLED=false: $HAS_PEER_DISABLED_FALSE"
echo

if [ "$HAS_PEER_DISABLED_FALSE" = "1" ]; then
  cat <<EOF
>> 结论 D: PEER_NETWORK_ENABLED=false 显式关掉了 peer 子系统
   修复: 删除或注释掉 PEER_NETWORK_ENABLED 那一行，然后:
         sudo systemctl restart $PRIMARY_UNIT
EOF
elif [ "$HAS_PEER_IN_PROCESS" -ge 1 ]; then
  cat <<EOF
>> 进程已经拿到了 PEER_SELF_ADDRESS。
   - 如果第 7 段里看到 "[peer] mounted ..."  → peer 已经启用，"disabled" 那条日志是另一个旧进程的残留
   - 如果还是只有 "[peer] disabled"          → 代码版本太老（< v0.3.0），需要升级 release
EOF
elif [ "$HAS_PEER_IN_FILE" = "1" ] && [ -n "$NODE_PIDS" ]; then
  cat <<EOF
>> 结论 B: .env 改了但 systemd 没把新值喂进当前 node 进程
   修复:
     sudo systemctl restart $PRIMARY_UNIT
     sudo journalctl -u $PRIMARY_UNIT -f
   重启后应看到 "[peer] mounted; self=..." 和 "[peer] announced self to N bootstrapped peer(s)"
EOF
elif [ "$HAS_PEER_IN_FILE" = "0" ] && [ -n "$ENV_FILE_PATHS" ]; then
  cat <<EOF
>> 结论 A: unit 引用的 EnvironmentFile 里没有 PEER_SELF_ADDRESS
   说明你改的是别的文件 / 写错了字段名。第 4 段顶部那些路径才是 systemd 实际读的。
   把 PEER_SELF_ADDRESS / PEER_CF_DISCOVERY_BASE_URL / PEER_STATE_DIR 加到那份 .env 末尾后:
     sudo systemctl restart $PRIMARY_UNIT
EOF
elif [ -z "$ENV_FILE_PATHS" ]; then
  cat <<EOF
>> 结论 C: 服务根本不是用 systemd EnvironmentFile 注入 env
   可能是宝塔 "项目管理" / pm2 / screen / nohup 起的。
   看第 5 段进程的 cmdline / cwd / exe，找出真正启动 node 的入口，env 要从那里加。
EOF
else
  echo ">> 信息不足，把整份输出发回 Claude 看。"
fi

hr "DONE"
