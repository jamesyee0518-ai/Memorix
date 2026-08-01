(function() {
  var style = getComputedStyle(document.documentElement);
  var accent = style.getPropertyValue('--accent').trim();
  var accent2 = style.getPropertyValue('--accent2').trim();
  var warn = style.getPropertyValue('--warn').trim();
  var danger = style.getPropertyValue('--danger').trim();
  var ink = style.getPropertyValue('--ink').trim();
  var muted = style.getPropertyValue('--muted').trim();
  var rule = style.getPropertyValue('--rule').trim();
  var bg2 = style.getPropertyValue('--bg2').trim();

  var fontFamily = "'InstrumentSans', 'PingFang SC', 'Noto Sans CJK SC', 'Microsoft YaHei', sans-serif";

  // --- Chart 1: Phase Completion (Stacked Bar) ---
  var chart1 = echarts.init(document.getElementById('chart-phase-completion'), null, { renderer: 'svg' });
  chart1.setOption({
    animation: false,
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, appendToBody: true },
    legend: { data: ['完整实现', '部分实现', '缺失'], bottom: 0, textStyle: { color: muted, fontSize: 12 } },
    grid: { left: 48, right: 24, top: 24, bottom: 40 },
    xAxis: { type: 'category', data: ['P0', 'P1', 'P2', 'P3'], axisLabel: { color: ink, fontSize: 13, fontWeight: 700 }, axisLine: { lineStyle: { color: rule } } },
    yAxis: { type: 'value', name: '交付物数量', nameTextStyle: { color: muted, fontSize: 11 }, axisLabel: { color: muted, fontSize: 11 }, splitLine: { lineStyle: { color: rule } } },
    series: [
      { name: '完整实现', type: 'bar', stack: 'total', data: [27, 15, 10, 11], itemStyle: { color: accent2 }, barWidth: '40%' },
      { name: '部分实现', type: 'bar', stack: 'total', data: [0, 0, 1, 1], itemStyle: { color: warn } },
      { name: '缺失', type: 'bar', stack: 'total', data: [0, 3, 0, 2], itemStyle: { color: danger } }
    ]
  });

  // --- Chart 2: Code Lines by Phase (Horizontal Bar) ---
  var chart2 = echarts.init(document.getElementById('chart-code-lines'), null, { renderer: 'svg' });
  chart2.setOption({
    animation: false,
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, appendToBody: true },
    grid: { left: 60, right: 40, top: 24, bottom: 24 },
    xAxis: { type: 'value', axisLabel: { color: muted, fontSize: 11 }, splitLine: { lineStyle: { color: rule } } },
    yAxis: { type: 'category', data: ['P3', 'P2', 'P1', 'P0'], axisLabel: { color: ink, fontSize: 13, fontWeight: 700 }, axisLine: { lineStyle: { color: rule } } },
    series: [{
      type: 'bar',
      data: [1510, 916, 1229, 4593],
      itemStyle: { color: accent, borderRadius: [0, 4, 4, 0] },
      barWidth: '50%',
      label: { show: true, position: 'right', color: ink, fontSize: 12, fontWeight: 700 }
    }]
  });

  // --- Chart 3: Overall Status (Pie) ---
  var chart3 = echarts.init(document.getElementById('chart-overall-status'), null, { renderer: 'svg' });
  chart3.setOption({
    animation: false,
    tooltip: { trigger: 'item', appendToBody: true, formatter: '{b}: {c} ({d}%)' },
    legend: { bottom: 0, textStyle: { color: muted, fontSize: 12 } },
    series: [{
      type: 'pie',
      radius: ['40%', '70%'],
      center: ['50%', '45%'],
      avoidLabelOverlap: true,
      itemStyle: { borderRadius: 6, borderColor: bg2, borderWidth: 2 },
      label: { show: true, color: ink, fontSize: 13, fontWeight: 700, formatter: '{b}\n{c}' },
      labelLine: { show: true, lineStyle: { color: rule } },
      data: [
        { value: 63, name: '完整实现', itemStyle: { color: accent2 } },
        { value: 2, name: '部分实现', itemStyle: { color: warn } },
        { value: 5, name: '缺失', itemStyle: { color: danger } }
      ]
    }]
  });

  window.addEventListener('resize', function() {
    chart1.resize();
    chart2.resize();
    chart3.resize();
  });
})();
