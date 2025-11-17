#!/usr/bin/env python3
"""
Load test runner for DeltaList PoC
Orchestrates multiple device simulators and collects metrics
"""

import subprocess
import time
import argparse
import signal
import sys
import json
from datetime import datetime
from pathlib import Path

class LoadTestRunner:
    def __init__(self, args):
        self.args = args
        self.processes = []
        self.start_time = None

    def run(self):
        """Run the load test"""
        print(f"========== DeltaList Load Test ==========")
        print(f"PoC Type: {self.args.poc_type}")
        print(f"Server: {self.args.server}")
        print(f"Total devices: {self.args.total_devices}")
        print(f"Simulators: {self.args.simulators}")
        print(f"Duration: {self.args.duration}s")
        print(f"Batch interval: {self.args.batch_interval}s")
        print(f"Events per batch: {self.args.events_per_batch}")
        print(f"=========================================\n")

        # Register signal handler for graceful shutdown
        signal.signal(signal.SIGINT, self._signal_handler)

        self.start_time = datetime.now()

        # Calculate devices per simulator
        devices_per_sim = self.args.total_devices // self.args.simulators
        remainder = self.args.total_devices % self.args.simulators

        # Start simulators
        for i in range(self.args.simulators):
            device_count = devices_per_sim + (1 if i < remainder else 0)
            device_offset = i * devices_per_sim + min(i, remainder)

            self._start_simulator(i, device_count, device_offset)
            time.sleep(0.5)  # Stagger starts

        print(f"\n✓ Started {len(self.processes)} simulator(s)")
        print(f"  Total devices: {self.args.total_devices}")
        print(f"  Press Ctrl+C to stop\n")

        # Wait for duration or termination
        try:
            if self.args.duration > 0:
                time.sleep(self.args.duration)
                print(f"\nDuration {self.args.duration}s elapsed. Stopping...")
                self._stop_all()
            else:
                # Wait indefinitely
                while True:
                    time.sleep(1)
        except KeyboardInterrupt:
            print("\nReceived Ctrl+C. Stopping...")
            self._stop_all()

        self._print_summary()

    def _start_simulator(self, index, device_count, device_offset):
        """Start a single simulator process"""
        if self.args.poc_type == "grpc":
            cmd = self._build_grpc_command(index, device_count, device_offset)
        else:
            cmd = self._build_mqtt_command(index, device_count, device_offset)

        print(f"Starting simulator #{index + 1}: {device_count} devices (offset: {device_offset})")

        # Create log file
        log_dir = Path("logs")
        log_dir.mkdir(exist_ok=True)
        log_file = log_dir / f"simulator_{index}_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"

        with open(log_file, 'w') as f:
            process = subprocess.Popen(
                cmd,
                stdout=f,
                stderr=subprocess.STDOUT
            )
            self.processes.append({
                'process': process,
                'index': index,
                'device_count': device_count,
                'log_file': log_file
            })

    def _build_grpc_command(self, index, device_count, device_offset):
        """Build command for gRPC simulator"""
        # Assuming the simulator binary is built and available
        cmd = [
            'dotnet', 'run',
            '--project', 'src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj',
            '--',
            '--server', self.args.server,
            '--devices', str(device_count),
            '--duration', str(self.args.duration),
            '--batch-interval', str(self.args.batch_interval),
            '--events-per-batch', str(self.args.events_per_batch),
            '--prefix', f'device-{index:03d}',
        ]

        if self.args.latency_ms > 0:
            cmd.extend(['--latency-ms', str(self.args.latency_ms)])

        if self.args.packet_loss > 0:
            cmd.extend(['--packet-loss', str(self.args.packet_loss)])

        return cmd

    def _build_mqtt_command(self, index, device_count, device_offset):
        """Build command for MQTT simulator"""
        # Extract host and port from server
        parts = self.args.server.split(':')
        host = parts[0]
        port = int(parts[1]) if len(parts) > 1 else 1883

        cmd = [
            'dotnet', 'run',
            '--project', 'src/MqttDeviceSimulator/MqttDeviceSimulator.csproj',
            '--',
            '--server', host,
            '--port', str(port),
            '--devices', str(device_count),
            '--duration', str(self.args.duration),
            '--batch-interval', str(self.args.batch_interval),
            '--events-per-batch', str(self.args.events_per_batch),
            '--prefix', f'device-{index:03d}',
        ]

        if self.args.latency_ms > 0:
            cmd.extend(['--latency-ms', str(self.args.latency_ms)])

        if self.args.packet_loss > 0:
            cmd.extend(['--packet-loss', str(self.args.packet_loss)])

        return cmd

    def _stop_all(self):
        """Stop all simulator processes"""
        for proc_info in self.processes:
            try:
                proc_info['process'].terminate()
                proc_info['process'].wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc_info['process'].kill()

        self.processes = []

    def _signal_handler(self, sig, frame):
        """Handle Ctrl+C"""
        print("\nReceived interrupt signal")
        self._stop_all()
        sys.exit(0)

    def _print_summary(self):
        """Print test summary and export results"""
        if self.start_time:
            duration = (datetime.now() - self.start_time).total_seconds()
            expected_batches = self.args.total_devices * (duration // self.args.batch_interval)
            expected_events = expected_batches * self.args.events_per_batch

            summary = {
                "test_type": self.args.poc_type,
                "start_time": self.start_time.isoformat(),
                "end_time": datetime.now().isoformat(),
                "duration_seconds": duration,
                "configuration": {
                    "total_devices": self.args.total_devices,
                    "simulators": self.args.simulators,
                    "batch_interval": self.args.batch_interval,
                    "events_per_batch": self.args.events_per_batch,
                    "latency_ms": self.args.latency_ms,
                    "packet_loss": self.args.packet_loss,
                },
                "expected_metrics": {
                    "batches": expected_batches,
                    "events": expected_events,
                },
                "log_files": [str(p['log_file']) for p in self.processes]
            }

            # Print summary
            print(f"\n========== Test Summary ==========")
            print(f"Duration: {duration:.2f}s")
            print(f"Total devices: {self.args.total_devices}")
            print(f"Expected batches: {expected_batches:.0f}")
            print(f"Expected events: {expected_events:.0f}")
            print(f"\nLog files saved in: logs/")

            # Export to JSON
            results_dir = Path("test_results")
            results_dir.mkdir(exist_ok=True)
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            results_file = results_dir / f"{self.args.poc_type}_load_test_{timestamp}.json"

            with open(results_file, 'w') as f:
                json.dump(summary, f, indent=2)

            print(f"Results exported to: {results_file}")
            print(f"==================================\n")

            # Generate graphs if matplotlib is available
            try:
                from generate_graphs import generate_load_test_graphs
                print("Generating graphs...")
                generate_load_test_graphs(str(results_file))
                print("Graphs generated successfully!")
            except ImportError:
                print("matplotlib not installed, skipping graph generation")
            except Exception as e:
                print(f"Graph generation failed: {e}")

def main():
    parser = argparse.ArgumentParser(description='DeltaList Load Test Runner')
    parser.add_argument('--poc-type', choices=['grpc', 'mqtt'], required=True,
                      help='PoC type to test')
    parser.add_argument('--server', required=True,
                      help='Server address (host:port for gRPC, host for MQTT)')
    parser.add_argument('--total-devices', type=int, default=1000,
                      help='Total number of devices to simulate')
    parser.add_argument('--simulators', type=int, default=10,
                      help='Number of simulator processes to spawn')
    parser.add_argument('--duration', type=int, default=300,
                      help='Test duration in seconds (0 = infinite)')
    parser.add_argument('--batch-interval', type=int, default=60,
                      help='Batch send interval in seconds')
    parser.add_argument('--events-per-batch', type=int, default=50,
                      help='Number of events per batch')
    parser.add_argument('--latency-ms', type=int, default=0,
                      help='Simulate network latency (0-N ms)')
    parser.add_argument('--packet-loss', type=float, default=0.0,
                      help='Simulate packet loss (0.0-1.0)')

    args = parser.parse_args()

    runner = LoadTestRunner(args)
    runner.run()

if __name__ == '__main__':
    main()
