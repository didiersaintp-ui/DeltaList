#!/usr/bin/env python3
"""
analyze_results.py - Analyze simulator metrics and generate comparison reports
Supports CSV parsing, statistics calculation, and HTML report generation
"""

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple
import re

def parse_metrics_csv(filepath: Path) -> Dict:
    """Parse metrics CSV file and extract key metrics"""
    metrics = {}

    with open(filepath, 'r') as f:
        reader = csv.reader(f)
        for row in reader:
            if len(row) >= 2 and not row[0].startswith('#'):
                key = row[0].strip()
                try:
                    value = float(row[1].strip())
                    metrics[key] = value
                except ValueError:
                    metrics[key] = row[1].strip()

    return metrics

def parse_latencies_csv(filepath: Path) -> List[float]:
    """Parse raw latencies CSV file"""
    latencies = []

    with open(filepath, 'r') as f:
        reader = csv.reader(f)
        next(reader)  # Skip header
        for row in reader:
            if row:
                try:
                    latencies.append(float(row[0]))
                except ValueError:
                    pass

    return latencies

def calculate_statistics(data: List[float]) -> Dict:
    """Calculate statistical metrics"""
    if not data:
        return {}

    sorted_data = sorted(data)
    n = len(sorted_data)

    def percentile(p):
        index = int(p * n) - 1
        return sorted_data[max(0, min(index, n - 1))]

    return {
        'count': n,
        'min': min(sorted_data),
        'max': max(sorted_data),
        'mean': sum(sorted_data) / n,
        'p50': percentile(0.50),
        'p95': percentile(0.95),
        'p99': percentile(0.99),
        'p999': percentile(0.999),
    }

def generate_html_report(grpc_metrics: Dict, mqtt_metrics: Dict, output_path: Path):
    """Generate HTML comparison report"""
    html = f"""
<!DOCTYPE html>
<html>
<head>
    <title>DeltaList PoC Comparison Report</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            margin: 40px;
            background-color: #f5f5f5;
        }}
        .container {{
            background-color: white;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        h1 {{
            color: #333;
            border-bottom: 3px solid #4CAF50;
            padding-bottom: 10px;
        }}
        h2 {{
            color: #555;
            margin-top: 30px;
        }}
        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
        }}
        th, td {{
            padding: 12px;
            text-align: left;
            border-bottom: 1px solid #ddd;
        }}
        th {{
            background-color: #4CAF50;
            color: white;
        }}
        tr:hover {{
            background-color: #f5f5f5;
        }}
        .better {{
            background-color: #c8e6c9;
            font-weight: bold;
        }}
        .metric-group {{
            margin: 30px 0;
        }}
        .summary {{
            background-color: #e3f2fd;
            padding: 20px;
            border-radius: 4px;
            margin: 20px 0;
        }}
        .winner {{
            font-size: 24px;
            color: #4CAF50;
            font-weight: bold;
        }}
    </style>
</head>
<body>
    <div class="container">
        <h1>DeltaList PoC Comparison Report</h1>
        <p>Generated: {Path().resolve()}</p>

        <h2>Overview</h2>
        <table>
            <tr>
                <th>Metric</th>
                <th>gRPC (PoC A)</th>
                <th>MQTT (PoC B)</th>
            </tr>
            <tr>
                <td>Total Batches Sent</td>
                <td>{grpc_metrics.get('batches_sent', 'N/A')}</td>
                <td>{mqtt_metrics.get('batches_sent', 'N/A')}</td>
            </tr>
            <tr>
                <td>Total Events Sent</td>
                <td>{grpc_metrics.get('events_sent', 'N/A')}</td>
                <td>{mqtt_metrics.get('events_sent', 'N/A')}</td>
            </tr>
            <tr>
                <td>Success Rate (%)</td>
                <td>{grpc_metrics.get('Success Rate (%)', 'N/A')}</td>
                <td>{mqtt_metrics.get('Success Rate (%)', 'N/A')}</td>
            </tr>
            <tr>
                <td>Throughput (batches/sec)</td>
                <td>{grpc_metrics.get('Throughput (batches/sec)', 'N/A'):.2f}</td>
                <td>{mqtt_metrics.get('Throughput (batches/sec)', 'N/A'):.2f}</td>
            </tr>
        </table>

        <h2>Latency Statistics (ms)</h2>
        <table>
            <tr>
                <th>Percentile</th>
                <th>gRPC</th>
                <th>MQTT</th>
            </tr>
            <tr>
                <td>Mean</td>
                <td>{grpc_metrics.get('Latency Mean (ms)', 'N/A'):.2f}</td>
                <td>{mqtt_metrics.get('Latency Mean (ms)', 'N/A'):.2f}</td>
            </tr>
            <tr>
                <td>P50 (Median)</td>
                <td>{grpc_metrics.get('Latency P50 (ms)', 'N/A'):.2f}</td>
                <td>{mqtt_metrics.get('Latency P50 (ms)', 'N/A'):.2f}</td>
            </tr>
            <tr>
                <td>P95</td>
                <td>{grpc_metrics.get('Latency P95 (ms)', 'N/A'):.2f}</td>
                <td>{mqtt_metrics.get('Latency P95 (ms)', 'N/A'):.2f}</td>
            </tr>
            <tr>
                <td>P99</td>
                <td>{grpc_metrics.get('Latency P99 (ms)', 'N/A'):.2f}</td>
                <td>{mqtt_metrics.get('Latency P99 (ms)', 'N/A'):.2f}</td>
            </tr>
            <tr>
                <td>P99.9</td>
                <td>{grpc_metrics.get('Latency P99.9 (ms)', 'N/A'):.2f}</td>
                <td>{mqtt_metrics.get('Latency P99.9 (ms)', 'N/A'):.2f}</td>
            </tr>
        </table>

        <div class="summary">
            <h2>Recommendation</h2>
            <p>Based on the metrics collected, here's the comparison:</p>
            <ul>
                <li><strong>Throughput:</strong>
                    {"gRPC" if grpc_metrics.get('Throughput (batches/sec)', 0) > mqtt_metrics.get('Throughput (batches/sec)', 0) else "MQTT"}
                    has higher throughput
                </li>
                <li><strong>Latency P99:</strong>
                    {"gRPC" if grpc_metrics.get('Latency P99 (ms)', float('inf')) < mqtt_metrics.get('Latency P99 (ms)', float('inf')) else "MQTT"}
                    has lower P99 latency
                </li>
                <li><strong>Success Rate:</strong>
                    {"gRPC" if grpc_metrics.get('Success Rate (%)', 0) > mqtt_metrics.get('Success Rate (%)', 0) else "MQTT"}
                    has higher success rate
                </li>
            </ul>
        </div>
    </div>
</body>
</html>
"""

    with open(output_path, 'w') as f:
        f.write(html)

    print(f"HTML report generated: {output_path}")

def generate_comparison_charts(grpc_metrics: Dict, mqtt_metrics: Dict, output_dir: Path):
    """Generate comparison charts using matplotlib"""
    try:
        import matplotlib.pyplot as plt
        import matplotlib
        matplotlib.use('Agg')  # Non-interactive backend
    except ImportError:
        print("matplotlib not installed, skipping chart generation")
        return

    output_dir.mkdir(parents=True, exist_ok=True)

    # Latency comparison chart
    fig, ax = plt.subplots(figsize=(10, 6))
    percentiles = ['P50', 'P95', 'P99', 'P99.9']
    grpc_values = [
        grpc_metrics.get('Latency P50 (ms)', 0),
        grpc_metrics.get('Latency P95 (ms)', 0),
        grpc_metrics.get('Latency P99 (ms)', 0),
        grpc_metrics.get('Latency P99.9 (ms)', 0),
    ]
    mqtt_values = [
        mqtt_metrics.get('Latency P50 (ms)', 0),
        mqtt_metrics.get('Latency P95 (ms)', 0),
        mqtt_metrics.get('Latency P99 (ms)', 0),
        mqtt_metrics.get('Latency P99.9 (ms)', 0),
    ]

    x = range(len(percentiles))
    width = 0.35

    ax.bar([i - width/2 for i in x], grpc_values, width, label='gRPC', color='#4CAF50')
    ax.bar([i + width/2 for i in x], mqtt_values, width, label='MQTT', color='#2196F3')

    ax.set_xlabel('Percentile')
    ax.set_ylabel('Latency (ms)')
    ax.set_title('Latency Comparison: gRPC vs MQTT')
    ax.set_xticks(x)
    ax.set_xticklabels(percentiles)
    ax.legend()
    ax.grid(True, alpha=0.3)

    plt.tight_layout()
    plt.savefig(output_dir / 'latency_comparison.png', dpi=150)
    plt.close()

    # Throughput comparison
    fig, ax = plt.subplots(figsize=(8, 6))
    protocols = ['gRPC', 'MQTT']
    throughputs = [
        grpc_metrics.get('Throughput (batches/sec)', 0),
        mqtt_metrics.get('Throughput (batches/sec)', 0),
    ]

    bars = ax.bar(protocols, throughputs, color=['#4CAF50', '#2196F3'])
    ax.set_ylabel('Throughput (batches/sec)')
    ax.set_title('Throughput Comparison')
    ax.grid(True, alpha=0.3, axis='y')

    # Add value labels on bars
    for bar in bars:
        height = bar.get_height()
        ax.text(bar.get_x() + bar.get_width()/2., height,
                f'{height:.2f}',
                ha='center', va='bottom')

    plt.tight_layout()
    plt.savefig(output_dir / 'throughput_comparison.png', dpi=150)
    plt.close()

    print(f"Charts generated in {output_dir}")

def main():
    parser = argparse.ArgumentParser(description='Analyze DeltaList PoC results')
    parser.add_argument('--grpc-dir', type=str, required=True, help='Directory containing gRPC metrics')
    parser.add_argument('--mqtt-dir', type=str, required=True, help='Directory containing MQTT metrics')
    parser.add_argument('--output', type=str, default='comparison_report.html', help='Output HTML file')
    parser.add_argument('--charts', action='store_true', help='Generate comparison charts')

    args = parser.parse_args()

    grpc_dir = Path(args.grpc_dir)
    mqtt_dir = Path(args.mqtt_dir)

    # Find metrics files
    grpc_metrics_file = list(grpc_dir.glob('grpc_metrics_*.csv'))
    mqtt_metrics_file = list(mqtt_dir.glob('mqtt_metrics_*.csv'))

    if not grpc_metrics_file:
        print(f"No gRPC metrics found in {grpc_dir}")
        sys.exit(1)

    if not mqtt_metrics_file:
        print(f"No MQTT metrics found in {mqtt_dir}")
        sys.exit(1)

    # Parse metrics
    print("Parsing metrics...")
    grpc_metrics = parse_metrics_csv(grpc_metrics_file[0])
    mqtt_metrics = parse_metrics_csv(mqtt_metrics_file[0])

    # Generate HTML report
    print("Generating HTML report...")
    output_path = Path(args.output)
    generate_html_report(grpc_metrics, mqtt_metrics, output_path)

    # Generate charts if requested
    if args.charts:
        print("Generating charts...")
        charts_dir = output_path.parent / 'charts'
        generate_comparison_charts(grpc_metrics, mqtt_metrics, charts_dir)

    print("\nAnalysis complete!")
    print(f"  HTML Report: {output_path}")
    if args.charts:
        print(f"  Charts: {charts_dir}")

if __name__ == '__main__':
    main()
