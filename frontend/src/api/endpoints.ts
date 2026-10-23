import { http } from './client'
import type * as T from './types'

const get = <R>(url: string, params?: object) => http.get<R>(url, { params }).then((r) => r.data)
const post = <R>(url: string, body?: unknown, headers?: Record<string, string>) => http.post<R>(url, body, { headers }).then((r) => r.data)
const put = <R>(url: string, body?: unknown) => http.put<R>(url, body).then((r) => r.data)
const patch = <R>(url: string, body?: unknown) => http.patch<R>(url, body).then((r) => r.data)
const del = (url: string) => http.delete(url).then(() => undefined)

/** Removes empty values so the query string stays clean. */
function clean<Q extends object>(query: Q): Partial<Q> {
  return Object.fromEntries(Object.entries(query).filter(([, v]) => v !== undefined && v !== null && v !== '')) as Partial<Q>
}

export const api = {
  auth: {
    register: (body: { firstName: string; lastName: string; email: string; password: string; phoneNumber?: string | null }) => post<T.AuthResponse>('/api/auth/register', body),
    login: (email: string, password: string) => post<T.AuthResponse>('/api/auth/login', { email, password }),
    logout: () => post<void>('/api/auth/logout'),
    me: () => get<T.User>('/api/auth/me'),
  },
  account: {
    profile: () => get<T.Profile>('/api/account/profile'),
    updateProfile: (body: T.UpdateProfile) => put<T.Profile>('/api/account/profile', body),
    changePassword: (currentPassword: string, newPassword: string) => post<void>('/api/account/change-password', { currentPassword, newPassword }),
    notifications: () => get<T.Notification[]>('/api/account/notifications'),
    markRead: () => post<void>('/api/account/notifications/read'),
  },
  contact: (body: { name: string; email: string; subject: string; message: string }) => post<void>('/api/contact', body),

  catalog: {
    categories: () => get<T.Category[]>('/api/categories'),
    locations: () => get<T.Location[]>('/api/locations'),
    search: (q: T.VehicleSearch) => get<T.PagedResult<T.VehicleSummary>>('/api/vehicles', clean(q)),
    featured: (pickupAt?: string, returnAt?: string) => get<T.VehicleSummary[]>('/api/vehicles/featured', clean({ pickupAt, returnAt })),
    vehicle: (id: string) => get<T.VehicleDetail>(`/api/vehicles/${id}`),
    availability: (id: string, pickupAt: string, returnAt: string) => get<T.Availability>(`/api/vehicles/${id}/availability`, { pickupAt, returnAt }),
    reviews: (id: string, query: { rating?: number; sort?: string; page?: number; pageSize?: number }) =>
      get<T.PagedResult<T.Review>>(`/api/vehicles/${id}/reviews`, clean(query)),
  },

  bookings: {
    quote: (body: T.QuoteRequest) => post<T.Quote>('/api/bookings/quote', body),
    create: (body: T.QuoteRequest) => post<T.BookingDetail>('/api/bookings', body),
    mine: (scope: 'upcoming' | 'past' | 'cancelled' | 'all') => get<T.BookingSummary[]>('/api/bookings/mine', { scope }),
    detail: (reference: string) => get<T.BookingDetail>(`/api/bookings/${reference}`),
    pay: (reference: string, idempotencyKey: string, simulateFailure = false) =>
      post<T.BookingDetail>(`/api/bookings/${reference}/pay`, { simulateFailure }, { 'Idempotency-Key': idempotencyKey }),
    cancellation: (reference: string) => get<T.CancellationPreview>(`/api/bookings/${reference}/cancellation`),
    cancel: (reference: string, reason: string | null) => post<T.BookingDetail>(`/api/bookings/${reference}/cancel`, { reason }),
    review: (reference: string, body: T.ReviewInput) => post<T.Review>(`/api/bookings/${reference}/review`, body),
  },

  reviews: {
    update: (id: string, body: T.ReviewInput) => put<T.Review>(`/api/reviews/${id}`, body),
    remove: (id: string) => del(`/api/reviews/${id}`),
    report: (id: string, reason: string) => post<void>(`/api/reviews/${id}/report`, { reason }),
  },

  recommendations: {
    recommend: (body: T.RecommendationRequest) => post<T.RecommendationResponse>('/api/recommendations', body),
    interpret: (text: string) => post<T.InterpretedPreferences>('/api/recommendations/interpret', { text }),
  },

  admin: {
    vehicles: (q: { q?: string; category?: string; status?: string; includeArchived?: boolean; page?: number; pageSize?: number }) =>
      get<T.PagedResult<T.AdminVehicle>>('/api/admin/vehicles', clean(q)),
    vehicle: (id: string) => get<T.AdminVehicle>(`/api/admin/vehicles/${id}`),
    createVehicle: (body: T.UpsertVehicle) => post<T.AdminVehicle>('/api/admin/vehicles', body),
    updateVehicle: (id: string, body: T.UpsertVehicle) => put<T.AdminVehicle>(`/api/admin/vehicles/${id}`, body),
    setVehicleStatus: (id: string, status: string) => patch<T.AdminVehicle>(`/api/admin/vehicles/${id}/status`, { status }),
    archiveVehicle: (id: string) => del(`/api/admin/vehicles/${id}`),
    vehicleHistory: (id: string) => get<T.VehicleHistory>(`/api/admin/vehicles/${id}/history`),
    uploadMedia: (id: string, file: File, kind: string, altText: string) => {
      const form = new FormData()
      form.append('file', file)
      form.append('kind', kind)
      form.append('altText', altText)
      return post<T.Media>(`/api/admin/vehicles/${id}/media`, form)
    },
    setPrimaryMedia: (id: string, mediaId: string) => put<void>(`/api/admin/vehicles/${id}/media/${mediaId}/primary`),
    deleteMedia: (id: string, mediaId: string) => del(`/api/admin/vehicles/${id}/media/${mediaId}`),

    maintenance: (q: { vehicleId?: string; status?: string; page?: number; pageSize?: number }) =>
      get<T.PagedResult<T.Maintenance>>('/api/admin/maintenance', clean(q)),
    alerts: () => get<T.MaintenanceAlert[]>('/api/admin/maintenance/alerts'),
    createMaintenance: (body: T.UpsertMaintenance) => post<T.Maintenance>('/api/admin/maintenance', body),
    startMaintenance: (id: string) => post<T.Maintenance>(`/api/admin/maintenance/${id}/start`),
    completeMaintenance: (id: string, body: { completedAt?: string | null; odometerKm?: number | null; cost?: number | null; nextServiceDueKm?: number | null; nextServiceDueDate?: string | null }) =>
      post<T.Maintenance>(`/api/admin/maintenance/${id}/complete`, body),
    cancelMaintenance: (id: string) => post<void>(`/api/admin/maintenance/${id}/cancel`),

    bookings: (q: { q?: string; status?: string; vehicleId?: string; from?: string; to?: string; page?: number; pageSize?: number }) =>
      get<T.PagedResult<T.BookingSummary>>('/api/admin/bookings', clean(q)),
    booking: (reference: string) => get<T.BookingDetail>(`/api/admin/bookings/${reference}`),
    changeBookingStatus: (reference: string, body: { status: string; reason?: string | null; odometerKm?: number | null }) =>
      post<T.BookingDetail>(`/api/admin/bookings/${reference}/status`, body),

    customers: (q: { q?: string; role?: string; isActive?: boolean; page?: number; pageSize?: number }) =>
      get<T.PagedResult<T.AdminCustomer>>('/api/admin/customers', clean(q)),
    setCustomerActive: (id: string, isActive: boolean) => patch<T.AdminCustomer>(`/api/admin/customers/${id}/status`, { isActive }),
    setCustomerRole: (id: string, role: string, grant: boolean) => post<T.AdminCustomer>(`/api/admin/customers/${id}/roles`, { role, grant }),
    auditLog: (q: { action?: string; entityType?: string; page?: number; pageSize?: number }) =>
      get<T.PagedResult<T.AuditEntry>>('/api/admin/audit-log', clean(q)),

    messages: (handled?: boolean, page = 1) => get<T.PagedResult<T.ContactMessage>>('/api/admin/messages', clean({ handled, page })),
    setMessageHandled: (id: string, isHandled: boolean) => patch<void>(`/api/admin/messages/${id}`, { isHandled }),

    reviews: (status: string, page = 1) => get<T.PagedResult<T.AdminReview>>('/api/admin/reviews', { status, page }),
    reanalyse: () => post<{ analysed: number }>('/api/admin/reviews/reanalyse'),
    moderate: (id: string, action: string, note: string | null) => post<void>(`/api/admin/reviews/${id}/moderate`, { action, note }),

    categories: () => get<T.Category[]>('/api/admin/categories'),
    saveCategory: (id: number | null, body: { name: string; slug: string; description: string; sortOrder: number }) =>
      id ? put<T.Category>(`/api/admin/categories/${id}`, body) : post<T.Category>('/api/admin/categories', body),
    deleteCategory: (id: number) => del(`/api/admin/categories/${id}`),
    locations: () => get<T.AdminLocation[]>('/api/admin/locations'),
    saveLocation: (id: number | null, body: Omit<T.AdminLocation, 'id'>) =>
      id ? put<T.AdminLocation>(`/api/admin/locations/${id}`, body) : post<T.AdminLocation>('/api/admin/locations', body),
  },

  analytics: {
    dashboard: () => get<T.Dashboard>('/api/admin/analytics/dashboard'),
    demand: (q: AnalyticsQuery) => get<T.Demand>('/api/admin/analytics/demand', clean(q)),
    utilisation: (q: AnalyticsQuery) => get<T.Utilisation>('/api/admin/analytics/utilisation', clean(q)),
    revenue: (q: AnalyticsQuery) => get<T.Revenue>('/api/admin/analytics/revenue', clean(q)),
    sentiment: (q: AnalyticsQuery) => get<T.Sentiment>('/api/admin/analytics/sentiment', clean(q)),
    forecast: (category?: string, horizonWeeks = 4) => get<T.Forecast>('/api/admin/analytics/forecast', clean({ category, horizonWeeks })),
    insights: (q: AnalyticsQuery) => get<T.Insights>('/api/admin/analytics/insights', clean(q)),
  },
}

export interface AnalyticsQuery { from?: string; to?: string; category?: string }

/** Downloads a CSV through the authenticated client and hands it to the browser as a file. */
export async function downloadCsv(url: string, params: object, fallbackName: string) {
  const response = await http.get<Blob>(url, { params: clean(params), responseType: 'blob' })
  const disposition = String(response.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const name = match ? decodeURIComponent(match[1]) : fallbackName
  const href = URL.createObjectURL(response.data)
  const link = document.createElement('a')
  link.href = href
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(href), 1000)
}
