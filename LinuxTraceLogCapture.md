# Overview - Linux Trace and Log Capture

This provides a quick start on how to capture logs on Linux. 

Logs:

- [LTTng](https://lttng.org) system trace (requires customized image for boot scenario)
- [perf](https://perf.wiki.kernel.org/)
- [cloud-init.log](https://cloud-init.io/)
  - Automatically logged by cloud-init to /var/log/cloud-init.log
- [dmesg.iso.log](https://en.wikipedia.org/wiki/Dmesg)
  - Standard auto dmesg log doesn't output in absolute time needed for correlation with other logs
  - dmesg --time-format iso > dmesg.iso.log
- [waagent.log](https://github.com/Azure/WALinuxAgent)
  - Automatically logged by WaLinuxAgent to /var/log/waagent.log
  - LogLevel can be turned more verbose in custom image
  - /etc/waagent.conf
  - Logs.Verbose=y
- [AndroidLogcat](https://developer.android.com/studio/command-line/logcat)
  - Default log format should be supported
  - Basic durations are supported/parsed on production builds / logs.
    - E.g. "SurfaceFlinger: Boot is finished (9550 ms)"
  - Enhanced durations are supported/parsed, if the logging includes init timing or *Timing logs such as SystemServerTiming. Perf durations are available in-development with [userdebug builds](https://source.android.com/setup/develop/new-device#userdebug-guidelines). 
      - E.g "SystemServerTimingAsync: InitThreadPoolExec:prepareAppData took to complete: 149ms"
  - Logcat can optionally log the year but defaults to not. If the year is not provided the year is assumed to be the current year on the analysis machine.
    - If this is incorrect, for example trace was captured in 2021, but analyzed in 2022 then the year will be interpreted incorrectly. 
    - This applies only if you need correct absolute timestamps, as relative timestamps will still be good.
    - Manual workaround: In the logcat log search/replace to add year. E.g. "12-21" -> "12-21-2021"
  - Logs are logged in local time zone and default assumed to be loaded in same time zone as captured
  - To provide a hint on the timezone if in a different zone
    - Place a "utcoffset.txt" file in the same folder as the trace. Place the UTC+Offset in the file as a double in hours. 
    - E.g For India Standard Time (IST) offset is UTC+5.5 so place "5.5" in the file. If logs are in UTC place 0 in the file

# LTTng
[LTTng](https://lttng.org) (Kernel CPU scheduling, Processes, Threads, Block IO/Disk, Syscalls, File events, etc)

[LTTng Docs](https://lttng.org/docs/v2.10/) [LTTng](https://lttng.org/) is an open source tracing framework for Linux. Installation instructions for your Linux distro can be found in the docs. 

Supports:
- Threads and Processes
- Context Switches / CPU Usage
- Syscalls
- File related events
- Block IO / Disk Activity
- Diagnostic Messages

Once you have everything set up you just need to decide what kind of information you are looking for and begin tracing. 

In this example we are looking at process scheduler events. We might use this to determine process lifetime and identify dependencies. You can learn more about what kind of "events" you can enable [here](https://lttng.org/man/1/lttng-enable-event/v2.8/). 
```bash
 root@xenial:~/tracing# lttng list --kernel # Gives a list of all Kernel events you can trace
 root@xenial:~/tracing# lttng list --kernel --syscall # Gives a list of all traceable Linux system calls

 root@xenial:~/tracing# lttng create my-kernel-session --output=/tmp/my-kernel-trace
 root@xenial:~/tracing# lttng enable-event --kernel sched_process*
 root@xenial:~/tracing# lttng start
 root@xenial:~/tracing# lttng stop
 root@xenial:~/tracing# lttng destroy
```

## Recommended LTTng Tracing 

### Install the tracing software:
Example on Ubuntu:
```bash
$ sudo apt-get install lttng-tools lttng-modules-dkms liblttng-ust-dev
```
For more examples see [LTTng Download docs](https://lttng.org/download/)

### Create a session:
```bash
$ sudo lttng create my-kernel-session --output=lttng-kernel-trace
```

### Add the desired events to be recorded:
```bash
$ sudo lttng enable-event --kernel block_rq_complete,block_rq_insert,block_rq_issue,printk_console,sched_wak*,sched_switch,sched_process_fork,sched_process_exit,sched_process_exec,lttng_statedump*
$ sudo lttng enable-event --kernel --syscall �-all
```

### Add context fields to the channel:
```bash
$ sudo lttng add-context --kernel --channel=channel0 --type=tid
$ sudo lttng add-context --kernel --channel=channel0 --type=pid
$ sudo lttng add-context --kernel --channel=channel0 --type=procname
```

### Start the recording:
```bash
$ sudo lttng start
```

### Save the session:
```bash
$ sudo lttng regenerate statedump <- Better correlation / info in Microsoft-Performance-Tools-Linux
$ sudo lttng stop
$ sudo lttng destroy
```

# Perf
Perf is used to collect tracepoint events.

[perf](https://perf.wiki.kernel.org/)

If you want to trace .NET Core then you need [perfcollect](http://aka.ms/perfcollect) which capture CPU sampling and more

## Perf Install
```bash
$ sudo apt-get install linux-tools-common
```

## Record a trace
```bash
$ sudo /usr/bin/perf record -g -a -F 999 -e cpu-clock,sched:sched_stat_sleep,sched:sched_switch,sched:sched_process_exit -o perf_cpu.data
```

## Stop the Trace
```bash
$ Ctrl-C
```

# Transferring the files to Windows UI (optional)
You then need to transfer the perf files to a Windows box where WPA runs. The most important file is perf.data.txt

```bash
$ sudo chmod 777 -R perf_cpu.data
```

- Copy files from Linux to Windows box with WinSCP/SCP OR 
```bash
$ tar -czvf perf_cpu.tar.gz perf_cpu.data
```

- Open perf_cpu.data with WPA

# Presentations

If you want to see a demo or get more in-depth info on using these tools check out a talk given at the [Linux Tracing Summit](https://www.tracingsummit.org/ts/2019/):
>Linux & Windows Perf Analysis using WPA, ([slides](https://www.tracingsummit.org/ts/2019/files/Tracingsummit2019-wpa-berg-gibeau.pdf)) ([video](https://youtu.be/HUbVaIi-aaw))
