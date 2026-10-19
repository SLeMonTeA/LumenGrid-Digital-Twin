<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

const apiBase = import.meta.env.VITE_API_BASE_URL || ''
const devices = ref([])
const loading = ref(false)
const errorMessage = ref('')
const connectionState = ref('连接中')
const integrationStatus = ref({ system: 'LumenGrid', generatedAt: null, channels: [] })
const integrationStatusLoading = ref(false)
const signalCenter = ref({ paused: false, intervalSeconds: 2, timeMultiplier: 1, lastTickAt: null, lastBatch: [], recentSignals: [] })
const settingsForm = reactive({ intervalSeconds: 2, timeMultiplier: 1 })
const selectedDeviceCode = ref('')
const metricHistory = reactive({})
const alerts = ref([])
const maintenanceTasks = ref([])
const equipmentEvents = ref([])
const replaySnapshot = ref(null)
const eventLoading = ref(false)
const replayAt = ref('')
const selectedEvent = ref(null)
const stepReplayActive = ref(false)
const stepReplayIndex = ref(-1)
const eventCursor = ref('')
const eventHasMore = ref(false)
const loadingMoreEvents = ref(false)
let connection
let integrationStatusTimer

const statusLabel = {
  Stopped: '停止',
  Starting: '启动中',
  Running: '运行',
  Degraded: '性能下降',
  Faulted: '异常',
  Maintenance: '维护中',
  Offline: '离线'
}

const scenarioLabel = {
  Normal: '正常运行',
  HighTemperature: '高温异常',
  Vibration: '振动异常',
  Offline: '设备断线',
  CapacityDrop: '产能下降'
}

const alertMetricLabel = { Temperature: '温度', Vibration: '振动', Heartbeat: '心跳', Capacity: '产能' }
const alertSeverityLabel = { Info: '提示', Warning: '警告', Critical: '严重' }
const alertStatusLabel = { New: '新建', Confirmed: '已确认', Processing: '处理中', Resolved: '已解决', Closed: '已关闭' }
const maintenanceStatusLabel = { Created: '待开始', InProgress: '维修中', Completed: '已完成', Cancelled: '已取消' }
const eventTypeLabel = {
  MetricSample: '指标采集',
  DeviceStarted: '设备启动',
  DeviceStopped: '设备停止',
  DeviceOnline: '设备上线',
  DeviceOffline: '设备离线',
  StatusChanged: '状态变更',
  ScenarioChanged: '场景切换',
  DeviceRecovered: '设备恢复',
  DeviceReset: '设备重置',
  AlertTriggered: '告警产生',
  AlertAcknowledged: '告警确认',
  AlertProcessing: '告警处理中',
  AlertResolved: '告警解决',
  AlertClosed: '告警关闭',
  MaintenanceCreated: '创建维护',
  MaintenanceStarted: '开始维护',
  MaintenanceCompleted: '维护完成',
  RuleChanged: '规则变更'
}

const chronologicalEvents = computed(() => [...equipmentEvents.value].reverse())
const replayMetricSeries = computed(() => {
  const series = { temperature: [], vibration: [], produced: [] }
  for (const event of chronologicalEvents.value) {
    const snapshot = readEventSnapshot(event)
    if (!snapshot) continue
    series.temperature.push(Number(snapshot.temperatureCelsius))
    series.vibration.push(Number(snapshot.vibrationMillimetersPerSecond))
    series.produced.push(Number(snapshot.producedQuantity))
  }
  return series
})
const selectedEventPayload = computed(() => {
  if (!selectedEvent.value?.payload) return ''
  try {
    return JSON.stringify(JSON.parse(selectedEvent.value.payload), null, 2)
  } catch {
    return selectedEvent.value.payload
  }
})

const summary = computed(() => ({
  total: devices.value.length,
  running: devices.value.filter(x => x.status === 'Running').length,
  stopped: devices.value.filter(x => ['Stopped', 'Offline'].includes(x.status)).length,
  alarm: devices.value.filter(x => ['Degraded', 'Faulted'].includes(x.status)).length,
  capacityLossPerHour: devices.value
    .filter(x => ['Degraded', 'Faulted', 'Offline'].includes(x.status))
    .reduce((total, x) => total + Number(x.ratedCapacityPerHour || 0), 0)
}))

async function request(path, options = {}) {
  const response = await fetch(`${apiBase}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options
  })
  const body = await response.text()
  if (!response.ok) throw new Error(body || `HTTP ${response.status}`)
  return body ? JSON.parse(body) : null
}

async function loadDevices() {
  loading.value = true
  errorMessage.value = ''
  try {
    devices.value = await request('/api/equipment')
    rememberSnapshots(devices.value)
  } catch (error) {
    errorMessage.value = `设备加载失败：${error.message}`
  } finally {
    loading.value = false
  }
}

function rememberSnapshots(snapshots) {
  for (const device of snapshots) {
    const history = metricHistory[device.deviceCode] || (metricHistory[device.deviceCode] = {
      lastSnapshotAt: null,
      temperature: [],
      vibration: [],
      produced: []
    })
    if (history.lastSnapshotAt === device.snapshotAt) continue
    history.lastSnapshotAt = device.snapshotAt
    appendPoint(history.temperature, Number(device.temperatureCelsius))
    appendPoint(history.vibration, Number(device.vibrationMillimetersPerSecond))
    appendPoint(history.produced, Number(device.producedQuantity))
  }
}

function appendPoint(points, value) {
  points.push(value)
  if (points.length > 30) points.shift()
}

function clearMetricHistory() {
  for (const key of Object.keys(metricHistory)) delete metricHistory[key]
}

function trendPoints(deviceCode, metric, width = 300, height = 64) {
  const values = metricHistory[deviceCode]?.[metric] || []
  if (values.length < 2) return ''
  const min = Math.min(...values)
  const max = Math.max(...values)
  const padding = Math.max((max - min) * 0.15, metric === 'produced' ? 0.05 : 0.5)
  const low = min - padding
  const range = Math.max(max - min + padding * 2, 1)
  return values.map((value, index) => {
    const x = (index / (values.length - 1)) * width
    const y = height - ((value - low) / range) * height
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
}

async function loadSimulationStatus() {
  try {
    const status = await request('/api/simulation/status?limit=12')
    signalCenter.value = status
    settingsForm.intervalSeconds = status.intervalSeconds
    settingsForm.timeMultiplier = status.timeMultiplier
  } catch (error) {
    errorMessage.value = `信号中心加载失败：${error.message}`
  }
}

async function loadAlerts() {
  try {
    alerts.value = await request('/api/alerts')
  } catch (error) {
    errorMessage.value = `告警加载失败：${error.message}`
  }
}

async function loadMaintenanceTasks() {
  try {
    maintenanceTasks.value = await request('/api/maintenance-tasks')
  } catch (error) {
    errorMessage.value = `维护任务加载失败：${error.message}`
  }
}

async function loadIntegrationStatus() {
  integrationStatusLoading.value = true
  try {
    integrationStatus.value = await request('/api/integration/status')
  } catch (error) {
    errorMessage.value = `集成状态加载失败：${error.message}`
  } finally {
    integrationStatusLoading.value = false
  }
}

function integrationState(channel) {
  if (channel.failed > 0) return 'failed'
  if (channel.pending > 0) return 'pending'
  return 'healthy'
}

function integrationStateLabel(channel) {
  const state = integrationState(channel)
  return state === 'failed' ? '有失败记录' : state === 'pending' ? '等待处理' : '运行正常'
}

async function loadEquipmentHistory() {
  if (!selectedDeviceCode.value) return
  eventLoading.value = true
  try {
    const devicePath = encodeURIComponent(selectedDeviceCode.value)
    const replayAtIso = replayAt.value ? new Date(replayAt.value).toISOString() : ''
    const eventQuery = new URLSearchParams({ limit: '40' })
    if (replayAtIso) eventQuery.set('to', replayAtIso)
    const historyQuery = replayAtIso ? '?at=' + encodeURIComponent(replayAtIso) : ''
    const [eventPage, replay] = await Promise.all([
      request('/api/equipment/' + devicePath + '/events/page?' + eventQuery.toString()),
      request('/api/equipment/' + devicePath + '/history' + historyQuery)
    ])
    equipmentEvents.value = eventPage.items
    eventCursor.value = eventPage.nextCursor || ''
    eventHasMore.value = Boolean(eventPage.nextCursor)
    replaySnapshot.value = replay
    selectedEvent.value = null
    stepReplayActive.value = false
    stepReplayIndex.value = -1
  } catch (error) {
    errorMessage.value = '事件历史加载失败：' + error.message
  } finally {
    eventLoading.value = false
  }
}

async function loadMoreEvents() {
  if (!selectedDeviceCode.value || !eventCursor.value || loadingMoreEvents.value) return
  loadingMoreEvents.value = true
  try {
    const devicePath = encodeURIComponent(selectedDeviceCode.value)
    const replayAtIso = replayAt.value ? new Date(replayAt.value).toISOString() : ''
    const query = new URLSearchParams({ limit: '40', cursor: eventCursor.value })
    if (replayAtIso) query.set('to', replayAtIso)
    const eventPage = await request('/api/equipment/' + devicePath + '/events/page?' + query.toString())
    equipmentEvents.value = [...equipmentEvents.value, ...eventPage.items]
    eventCursor.value = eventPage.nextCursor || ''
    eventHasMore.value = Boolean(eventPage.nextCursor)
  } catch (error) {
    errorMessage.value = '更多事件加载失败：' + error.message
  } finally {
    loadingMoreEvents.value = false
  }
}

function readEventSnapshot(event) {
  try {
    return JSON.parse(event.payload || '{}').snapshot || null
  } catch {
    return null
  }
}

function historyTrendPoints(metric, width = 300, height = 64) {
  const values = replayMetricSeries.value[metric] || []
  if (values.length < 2) return ''
  const min = Math.min(...values)
  const max = Math.max(...values)
  const padding = Math.max((max - min) * 0.15, metric === 'produced' ? 0.05 : 0.5)
  const low = min - padding
  const range = Math.max(max - min + padding * 2, 1)
  return values.map((value, index) => {
    const x = (index / (values.length - 1)) * width
    const y = height - ((value - low) / range) * height
    return x.toFixed(1) + ',' + y.toFixed(1)
  }).join(' ')
}

function toLocalDateTimeInput(value) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  const offset = date.getTimezoneOffset()
  return new Date(date.getTime() - offset * 60000).toISOString().slice(0, 16)
}

function applyReplayEvent(event, index) {
  selectedEvent.value = event
  const snapshot = readEventSnapshot(event)
  if (!snapshot) return
  replayAt.value = toLocalDateTimeInput(event.occurredAt)
  replaySnapshot.value = {
    deviceCode: selectedDeviceCode.value,
    replayAt: event.occurredAt,
    hasSnapshot: true,
    snapshot,
    eventsApplied: index + 1,
    sourceEventId: event.eventId,
    sourceEventType: event.eventType,
    sourceEventAt: event.occurredAt
  }
}

function selectEvent(event) {
  const index = chronologicalEvents.value.findIndex(item => item.eventId === event.eventId)
  applyReplayEvent(event, Math.max(index, 0))
}

function startStepReplay() {
  if (!chronologicalEvents.value.length) return
  stepReplayActive.value = true
  stepReplayIndex.value = -1
  selectedEvent.value = null
}

function nextReplayEvent() {
  if (!stepReplayActive.value) startStepReplay()
  const nextIndex = stepReplayIndex.value + 1
  if (nextIndex >= chronologicalEvents.value.length) return
  stepReplayIndex.value = nextIndex
  applyReplayEvent(chronologicalEvents.value[nextIndex], nextIndex)
}

async function returnToLiveHistory() {
  replayAt.value = ''
  stepReplayActive.value = false
  stepReplayIndex.value = -1
  selectedEvent.value = null
  await loadEquipmentHistory()
}

async function control(device, action) {
  try {
    await request(`/api/simulation/devices/${encodeURIComponent(device.deviceCode)}/${action}`, { method: 'POST', body: '{}' })
    await loadDevices()
    await loadSimulationStatus()
  } catch (error) {
    errorMessage.value = `场景控制失败：${error.message}`
  }
}

async function controlSelected(action) {
  const device = devices.value.find(item => item.deviceCode === selectedDeviceCode.value)
  if (device) await control(device, action)
}

async function updateSimulationSettings() {
  try {
    const status = await request('/api/simulation/settings', {
      method: 'PUT',
      body: JSON.stringify({
        intervalSeconds: Number(settingsForm.intervalSeconds),
        timeMultiplier: Number(settingsForm.timeMultiplier)
      })
    })
    signalCenter.value = status
  } catch (error) {
    errorMessage.value = `信号参数更新失败：${error.message}`
  }
}

async function toggleSignalCenter() {
  try {
    const status = await request(`/api/simulation/${signalCenter.value.paused ? 'resume' : 'pause'}`, { method: 'POST', body: '{}' })
    signalCenter.value = status
  } catch (error) {
    errorMessage.value = `信号发送状态更新失败：${error.message}`
  }
}

async function resetSimulation() {
  try {
    await request('/api/simulation/reset', { method: 'POST', body: '{}' })
    clearMetricHistory()
    await loadDevices()
    await loadSimulationStatus()
  } catch (error) {
    errorMessage.value = `演示场景清理失败：${error.message}`
  }
}

async function loadDemoPreset() {
  try {
    await request('/api/simulation/presets/demo', { method: 'POST', body: '{}' })
    clearMetricHistory()
    await loadDevices()
    await loadSimulationStatus()
  } catch (error) {
    errorMessage.value = `预置场景加载失败：${error.message}`
  }
}

async function alertAction(alert, action) {
  try {
    await request(`/api/alerts/${encodeURIComponent(alert.alertCode)}/${action}`, {
      method: 'POST',
      body: JSON.stringify({ operator: 'dashboard-operator', reason: action === 'resolve' ? '看板确认已处理' : '' })
    })
    await loadAlerts()
  } catch (error) {
    errorMessage.value = `告警处理失败：${error.message}`
  }
}

function hasMaintenanceTask(alert) {
  return maintenanceTasks.value.some(task => task.alertCode === alert.alertCode && task.status !== 'Completed' && task.status !== 'Cancelled')
}

async function createMaintenanceTask(alert) {
  try {
    await request('/api/maintenance-tasks', {
      method: 'POST',
      body: JSON.stringify({ deviceCode: alert.deviceCode, alertCode: alert.alertCode, symptom: `${alertMetricLabel[alert.metric] || alert.metric}异常`, technician: 'maintenance-operator' })
    })
    await loadAlerts()
    await loadMaintenanceTasks()
  } catch (error) {
    errorMessage.value = `维护任务创建失败：${error.message}`
  }
}

async function startMaintenanceTask(task) {
  try {
    await request(`/api/maintenance-tasks/${encodeURIComponent(task.taskNo)}/start`, {
      method: 'POST',
      body: JSON.stringify({ operator: 'maintenance-operator' })
    })
    await loadMaintenanceTasks()
  } catch (error) {
    errorMessage.value = `维护任务启动失败：${error.message}`
  }
}

async function completeMaintenanceTask(task) {
  try {
    await request(`/api/maintenance-tasks/${encodeURIComponent(task.taskNo)}/complete`, {
      method: 'POST',
      body: JSON.stringify({ cause: '现场检查完成', result: '维修完成并恢复设备', technician: 'maintenance-operator', restoreEquipment: true })
    })
    await loadMaintenanceTasks()
    await loadAlerts()
  } catch (error) {
    errorMessage.value = `维护任务完成失败：${error.message}`
  }
}

async function scenario(device, name) {
  await control(device, `scenarios/${name}`)
}

function statusClass(status) {
  return status.toLowerCase()
}

onMounted(async () => {
  await loadDevices()
  if (devices.value.length) selectedDeviceCode.value = devices.value[0].deviceCode
  await loadSimulationStatus()
  await loadAlerts()
  await loadMaintenanceTasks()
  await loadIntegrationStatus()
  integrationStatusTimer = window.setInterval(loadIntegrationStatus, 15000)
  await loadEquipmentHistory()
  connection = new HubConnectionBuilder()
    .withUrl(`${apiBase}/hubs/equipment`)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
  connection.on('EquipmentSnapshotChanged', snapshots => {
    devices.value = snapshots
    rememberSnapshots(snapshots)
  })
  connection.on('SimulationSignalBatchChanged', status => {
    signalCenter.value = status
  })
  connection.on('AlertChanged', alert => {
    const index = alerts.value.findIndex(item => item.alertCode === alert.alertCode)
    if (!alert.isActive) {
      if (index >= 0) alerts.value.splice(index, 1)
      return
    }
    if (index >= 0) alerts.value[index] = alert
    else alerts.value.unshift(alert)
  })
  connection.on('MaintenanceChanged', task => {
    const index = maintenanceTasks.value.findIndex(item => item.taskNo === task.taskNo)
    if (task.status === 'Completed' || task.status === 'Cancelled') {
      if (index >= 0) maintenanceTasks.value.splice(index, 1)
    } else if (index >= 0) maintenanceTasks.value[index] = task
    else maintenanceTasks.value.unshift(task)
  })
  connection.onreconnecting(() => { connectionState.value = '重连中' })
  connection.onreconnected(() => { connectionState.value = '实时连接' })
  try {
    await connection.start()
    connectionState.value = '实时连接'
  } catch {
    connectionState.value = '离线读取'
  }
})

watch(selectedDeviceCode, async () => {
  replayAt.value = ''
  await loadEquipmentHistory()
})

onBeforeUnmount(() => {
  connection?.stop()
  if (integrationStatusTimer) window.clearInterval(integrationStatusTimer)
})
</script>

<template>
  <main class="page-shell">
    <header class="hero">
      <div>
        <p class="eyebrow">LUMENGRID · PHASE THREE</p>
        <h1>设备实时看板与事件回放</h1>
        <p class="subtitle">虚拟设备信号中心 · 状态、指标、事件与历史快照一屏观察</p>
      </div>
      <div class="connection"><span class="dot" :class="connectionState === '实时连接' ? 'online' : ''"></span>{{ connectionState }}</div>
    </header>

    <section class="summary-grid">
      <article><span>设备总数</span><strong>{{ summary.total }}</strong></article>
      <article class="good"><span>正常运行</span><strong>{{ summary.running }}</strong></article>
      <article class="warning"><span>告警 / 降级</span><strong>{{ summary.alarm }}</strong></article>
      <article class="muted"><span>停机 / 离线</span><strong>{{ summary.stopped }}</strong></article>
      <article><span>当前产能损失</span><strong>{{ summary.capacityLossPerHour.toFixed(0) }}<small> 件/小时</small></strong></article>
    </section>

    <section class="integration-panel">
      <div class="panel-head">
        <div>
          <p class="eyebrow">INTEGRATION CENTER · PHASE FOUR</p>
          <h2>跨系统集成状态</h2>
        </div>
        <div class="integration-toolbar">
          <span class="integration-updated">{{ integrationStatus.generatedAt ? `更新于 ${new Date(integrationStatus.generatedAt).toLocaleTimeString()}` : '尚未读取' }}</span>
          <button class="ghost" :disabled="integrationStatusLoading" @click="loadIntegrationStatus">{{ integrationStatusLoading ? '刷新中…' : '刷新状态' }}</button>
        </div>
      </div>
      <div v-if="integrationStatus.channels?.length" class="integration-grid">
        <article v-for="channel in integrationStatus.channels" :key="channel.channel" class="integration-card" :class="integrationState(channel)">
          <div class="integration-card-head">
            <div>
              <strong>{{ channel.channel }}</strong>
              <span>{{ channel.direction === 'inbound' ? '接收自' : '发送到' }} {{ channel.partner }}</span>
            </div>
            <b>{{ integrationStateLabel(channel) }}</b>
          </div>
          <div class="integration-counts">
            <span>待处理 <b>{{ channel.pending }}</b></span>
            <span>成功 <b>{{ channel.succeeded }}</b></span>
            <span>失败 <b>{{ channel.failed }}</b></span>
          </div>
          <p v-if="channel.lastError" class="integration-error">{{ channel.lastError }}</p>
          <p v-else class="integration-success">最近一次成功：{{ channel.lastSucceededAt ? new Date(channel.lastSucceededAt).toLocaleString() : '暂无记录' }}</p>
        </article>
      </div>
      <div v-else class="signals-empty">暂无集成状态。</div>
    </section>

    <section class="signal-panel">
      <div class="panel-head">
        <div>
          <p class="eyebrow">SIGNAL CENTER</p>
          <h2>模拟信号中心</h2>
        </div>
        <span class="sender-state" :class="signalCenter.paused ? 'paused' : 'sending'">
          {{ signalCenter.paused ? '已暂停发送' : '正在发送' }} · {{ signalCenter.intervalSeconds }} 秒 / 批
        </span>
      </div>
      <div class="signal-controls">
        <label>目标设备
          <select v-model="selectedDeviceCode">
            <option v-for="device in devices" :key="device.deviceCode" :value="device.deviceCode">{{ device.deviceCode }}</option>
          </select>
        </label>
        <button @click="controlSelected('start')">启动信号</button>
        <button class="ghost" @click="controlSelected('stop')">停止信号</button>
        <label>发送周期
          <input v-model.number="settingsForm.intervalSeconds" type="number" min="1" max="60" /> 秒
        </label>
        <label>时间倍率
          <input v-model.number="settingsForm.timeMultiplier" type="number" min="0.1" max="10" step="0.1" /> 倍
        </label>
        <button @click="updateSimulationSettings">应用参数</button>
        <button @click="toggleSignalCenter">{{ signalCenter.paused ? '恢复发送' : '暂停发送' }}</button>
        <button class="ghost" @click="resetSimulation">清理场景</button>
        <button class="ghost" @click="loadDemoPreset">加载演示预置</button>
      </div>
      <div class="signal-meta">
        <span>最近批次：{{ signalCenter.lastBatch?.length || 0 }} 台</span>
        <span>最近发送：{{ signalCenter.lastTickAt ? new Date(signalCenter.lastTickAt).toLocaleTimeString() : '暂无' }}</span>
      </div>
      <div v-if="signalCenter.recentSignals?.length" class="signals-table-wrap">
        <table class="signals-table">
          <thead><tr><th>设备</th><th>发送时间</th><th>状态</th><th>温度</th><th>振动</th><th>累计产量</th><th>故障码</th></tr></thead>
          <tbody>
            <tr v-for="signal in signalCenter.recentSignals.slice(0, 8)" :key="`${signal.deviceCode}-${signal.sentAt}`">
              <td>{{ signal.deviceCode }}</td>
              <td>{{ new Date(signal.sentAt).toLocaleTimeString() }}</td>
              <td>{{ statusLabel[signal.status] || signal.status }}</td>
              <td>{{ Number(signal.temperatureCelsius).toFixed(1) }} °C</td>
              <td>{{ Number(signal.vibrationMillimetersPerSecond).toFixed(1) }} mm/s</td>
              <td>{{ Number(signal.producedQuantity).toFixed(2) }}</td>
              <td class="fault">{{ signal.faultCode || '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="signals-empty">暂无发送记录。启动一台设备后，这里会显示最近信号。</div>
    </section>

    <section class="alerts-panel">
      <div class="panel-head">
        <div>
          <p class="eyebrow">ALERT CENTER</p>
          <h2>当前告警</h2>
        </div>
        <span class="alert-count">{{ alerts.length }} 条活动告警</span>
      </div>
      <div v-if="alerts.length" class="alert-list">
        <article v-for="alert in alerts" :key="alert.alertCode" class="alert-row" :class="alert.severity.toLowerCase()">
          <div class="alert-main">
            <div class="alert-title">
              <span class="severity-pill">{{ alertSeverityLabel[alert.severity] || alert.severity }}</span>
              <strong>{{ alert.deviceCode }} · {{ alert.ruleCode }}</strong>
              <span class="alert-status">{{ alertStatusLabel[alert.status] || alert.status }}</span>
            </div>
            <div class="alert-detail">
              {{ alertMetricLabel[alert.metric] || alert.metric }} {{ Number(alert.currentValue).toFixed(1) }}，阈值 {{ Number(alert.threshold).toFixed(1) }} · 触发于 {{ new Date(alert.triggeredAt).toLocaleTimeString() }}
            </div>
          </div>
          <div class="alert-actions">
            <button v-if="alert.status === 'New'" @click="alertAction(alert, 'acknowledge')">确认</button>
            <button v-if="['New', 'Confirmed'].includes(alert.status)" @click="alertAction(alert, 'start-processing')">处理中</button>
            <button v-if="alert.status !== 'Resolved' && alert.status !== 'Closed' && !hasMaintenanceTask(alert)" @click="createMaintenanceTask(alert)">建维护任务</button>
            <button v-if="['New', 'Confirmed', 'Processing'].includes(alert.status)" class="ghost" @click="alertAction(alert, 'resolve')">解决</button>
          </div>
        </article>
      </div>
      <div v-else class="signals-empty">当前没有活动告警。可以注入高温或振动场景观察规则触发。</div>
    </section>

    <section v-if="maintenanceTasks.length" class="maintenance-panel">
      <div class="panel-head">
        <div>
          <p class="eyebrow">MAINTENANCE</p>
          <h2>维护任务</h2>
        </div>
        <span class="alert-count">{{ maintenanceTasks.length }} 条进行中</span>
      </div>
      <div class="maintenance-list">
        <article v-for="task in maintenanceTasks" :key="task.taskNo" class="maintenance-row">
          <div>
            <div class="alert-title"><strong>{{ task.taskNo }} · {{ task.deviceCode }}</strong><span class="alert-status">{{ maintenanceStatusLabel[task.status] || task.status }}</span></div>
            <div class="alert-detail">{{ task.symptom || '设备维护' }} · 技术员：{{ task.technician || '未指定' }}</div>
          </div>
          <div class="alert-actions">
            <button v-if="task.status === 'Created'" @click="startMaintenanceTask(task)">开始维修</button>
            <button v-if="['Created', 'InProgress'].includes(task.status)" class="ghost" @click="completeMaintenanceTask(task)">完成维修</button>
          </div>
        </article>
      </div>
    </section>

    <section class="events-panel">
      <div class="panel-head">
        <div>
          <p class="eyebrow">EVENT TIMELINE · PHASE THREE</p>
          <h2>设备事件时间线</h2>
        </div>
        <div class="timeline-controls">
          <label>查看设备
            <select v-model="selectedDeviceCode">
              <option v-for="device in devices" :key="'timeline-' + device.deviceCode" :value="device.deviceCode">{{ device.deviceCode }}</option>
            </select>
          </label>
          <label>回放时间
            <input v-model="replayAt" type="datetime-local" />
          </label>
          <button @click="loadEquipmentHistory">查看历史</button>
          <button class="ghost" @click="startStepReplay">逐条回放</button>
          <button :disabled="!stepReplayActive || stepReplayIndex >= chronologicalEvents.length - 1" @click="nextReplayEvent">下一事件</button>
          <button class="ghost" @click="returnToLiveHistory">回到实时</button>
        </div>
      </div>
      <div class="replay-summary" v-if="replaySnapshot?.hasSnapshot">
        <span>{{ replayAt ? '历史回放' : '实时快照' }}</span>
        <span>历史事件：{{ replaySnapshot.eventsApplied }} 条</span>
        <span>当前重建状态：{{ statusLabel[replaySnapshot.snapshot.status] || replaySnapshot.snapshot.status }}</span>
        <span>重建产量：{{ Number(replaySnapshot.snapshot.producedQuantity).toFixed(2) }}</span>
        <span>快照时间：{{ new Date(replaySnapshot.sourceEventAt).toLocaleTimeString() }}</span>
      </div>
      <div v-if="stepReplayActive" class="replay-progress">
        正在逐条回放：第 {{ Math.max(stepReplayIndex + 1, 0) }} / {{ chronologicalEvents.length }} 条
      </div>
      <div v-if="replayMetricSeries.temperature.length > 1" class="replay-trends">
        <div class="replay-trend-card">
          <span>历史温度</span>
          <b>{{ replayMetricSeries.temperature.at(-1).toFixed(1) }} °C</b>
          <svg viewBox="0 0 300 64" preserveAspectRatio="none" aria-label="历史温度趋势">
            <polyline :points="historyTrendPoints('temperature')" class="trend-line temperature-line" />
          </svg>
        </div>
        <div class="replay-trend-card">
          <span>历史振动</span>
          <b>{{ replayMetricSeries.vibration.at(-1).toFixed(1) }} mm/s</b>
          <svg viewBox="0 0 300 64" preserveAspectRatio="none" aria-label="历史振动趋势">
            <polyline :points="historyTrendPoints('vibration')" class="trend-line vibration-line" />
          </svg>
        </div>
        <div class="replay-trend-card">
          <span>历史累计产量</span>
          <b>{{ replayMetricSeries.produced.at(-1).toFixed(2) }}</b>
          <svg viewBox="0 0 300 64" preserveAspectRatio="none" aria-label="历史累计产量趋势">
            <polyline :points="historyTrendPoints('produced')" class="trend-line produced-line" />
          </svg>
        </div>
      </div>
      <div v-if="eventLoading" class="signals-empty">正在读取事件历史…</div>
      <div v-else-if="equipmentEvents.length" class="event-list">
        <article v-for="event in equipmentEvents" :key="event.eventId" class="event-row" :class="{ selected: selectedEvent?.eventId === event.eventId }" @click="selectEvent(event)">
          <time>{{ new Date(event.occurredAt).toLocaleTimeString() }}</time>
          <span class="event-type">{{ eventTypeLabel[event.eventType] || event.eventType }}</span>
          <strong>{{ event.summary }}</strong>
          <small>{{ event.source }}</small>
        </article>
        <button v-if="eventHasMore" class="load-more-events" :disabled="loadingMoreEvents" @click="loadMoreEvents">
          {{ loadingMoreEvents ? '正在加载…' : '加载更早事件' }}
        </button>
      </div>
      <div v-else class="signals-empty">暂无事件。启动设备或注入场景后，这里会形成可回看的轨迹。</div>
      <div v-if="selectedEvent" class="event-detail">
        <div class="event-detail-head">
          <strong>{{ eventTypeLabel[selectedEvent.eventType] || selectedEvent.eventType }} · {{ selectedEvent.eventId }}</strong>
          <span>{{ new Date(selectedEvent.occurredAt).toLocaleString() }}</span>
        </div>
        <p>{{ selectedEvent.summary }} · 来源：{{ selectedEvent.source }}</p>
        <pre>{{ selectedEventPayload }}</pre>
      </div>
    </section>

    <p v-if="errorMessage" class="error">{{ errorMessage }}</p>
    <section v-if="loading && !devices.length" class="empty">正在读取设备档案…</section>
    <section v-else class="device-grid">
      <article v-for="device in devices" :key="device.deviceCode" class="device-card" :class="statusClass(device.status)">
        <div class="card-head">
          <div><h2>{{ device.name }}</h2><span class="code">{{ device.deviceCode }} · {{ device.workCenter }}</span></div>
          <span class="status-pill">{{ statusLabel[device.status] || device.status }}</span>
        </div>
        <div class="scenario">场景：{{ scenarioLabel[device.scenario] || device.scenario }}</div>
        <div class="metrics">
          <div><span>温度</span><b>{{ Number(device.temperatureCelsius).toFixed(1) }} °C</b></div>
          <div><span>电流</span><b>{{ Number(device.currentAmps).toFixed(1) }} A</b></div>
          <div><span>振动</span><b>{{ Number(device.vibrationMillimetersPerSecond).toFixed(1) }} mm/s</b></div>
          <div><span>节拍</span><b>{{ Number(device.cycleSeconds).toFixed(1) }} s</b></div>
          <div><span>累计产量</span><b>{{ Number(device.producedQuantity).toFixed(2) }}</b></div>
          <div><span>良品 / 不良</span><b>{{ Number(device.goodQuantity).toFixed(2) }} / {{ Number(device.defectQuantity).toFixed(2) }}</b></div>
        </div>
        <div class="trend-grid">
          <div class="trend-card">
            <div><span>温度趋势</span><b>{{ Number(device.temperatureCelsius).toFixed(1) }} °C</b></div>
            <svg viewBox="0 0 300 64" preserveAspectRatio="none" aria-label="温度趋势">
              <polyline :points="trendPoints(device.deviceCode, 'temperature')" class="trend-line temperature-line" />
            </svg>
          </div>
          <div class="trend-card">
            <div><span>振动趋势</span><b>{{ Number(device.vibrationMillimetersPerSecond).toFixed(1) }} mm/s</b></div>
            <svg viewBox="0 0 300 64" preserveAspectRatio="none" aria-label="振动趋势">
              <polyline :points="trendPoints(device.deviceCode, 'vibration')" class="trend-line vibration-line" />
            </svg>
          </div>
          <div class="trend-card">
            <div><span>累计产量趋势</span><b>{{ Number(device.producedQuantity).toFixed(2) }}</b></div>
            <svg viewBox="0 0 300 64" preserveAspectRatio="none" aria-label="累计产量趋势">
              <polyline :points="trendPoints(device.deviceCode, 'produced')" class="trend-line produced-line" />
            </svg>
          </div>
        </div>
        <div class="card-foot">
          <span v-if="device.faultCode" class="fault">{{ device.faultCode }}</span>
          <span v-else>{{ device.canReceiveProduction ? '可接收生产任务' : '暂不可接收任务' }}</span>
          <div class="actions">
            <button @click="control(device, 'start')">启动</button>
            <button @click="scenario(device, 'high-temperature')">高温</button>
            <button @click="scenario(device, 'vibration')">振动</button>
            <button @click="control(device, 'recover')">恢复</button>
            <button class="ghost" @click="control(device, 'stop')">停止</button>
          </div>
        </div>
      </article>
    </section>
  </main>
</template>
