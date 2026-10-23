import { zodResolver } from '@hookform/resolvers/zod'
import { Bell } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { api } from '../api/endpoints'
import type { Profile } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { Field } from '../components/Field'
import { Alert, PageHeader, Spinner } from '../components/ui'
import { formatDateTime } from '../lib/format'
import { applyServerErrors, passwordSchema, phoneSchema } from '../lib/forms'
import { useAsync } from '../lib/useAsync'

const optional = (schema: z.ZodString) => schema.optional().or(z.literal(''))
const profileSchema = z.object({
  firstName: z.string().trim().min(1, 'Required.').max(60),
  lastName: z.string().trim().min(1, 'Required.').max(60),
  phoneNumber: optional(phoneSchema),
  addressLine: optional(z.string().max(200)),
  suburb: optional(z.string().max(100)),
  city: optional(z.string().max(100)),
  province: optional(z.string().max(100)),
  postalCode: optional(z.string().max(10).regex(/^[0-9]*$/, 'Digits only.')),
  dateOfBirth: optional(z.string()),
  driverLicenceExpiry: optional(z.string()),
})
type ProfileValues = z.infer<typeof profileSchema>
const provinces = ['Eastern Cape', 'Free State', 'Gauteng', 'KwaZulu-Natal', 'Limpopo', 'Mpumalanga', 'North West', 'Northern Cape', 'Western Cape']

export default function AccountPage() {
  const profile = useAsync(() => api.account.profile(), [])
  const notifications = useAsync(() => api.account.notifications(), [])

  return (
    <div className="container-page max-w-5xl py-8">
      <PageHeader title="My account" subtitle="Your details are used for bookings only and are never shown publicly." />
      <div className="grid gap-6 lg:grid-cols-[1.4fr_1fr]">
        <div className="space-y-6">
          {profile.loading && <Spinner />}
          {profile.error && <Alert onRetry={profile.reload}>{profile.error}</Alert>}
          {profile.data && <ProfileForm profile={profile.data} />}
          <PasswordForm />
        </div>
        <section className="card h-fit p-5">
          <div className="flex items-center justify-between">
            <h2 className="flex items-center gap-2 text-lg"><Bell className="size-4" aria-hidden />Notifications</h2>
            {notifications.data?.some((n) => !n.isRead) && (
              <button className="btn-ghost btn-sm" onClick={async () => { await api.account.markRead(); notifications.reload() }}>Mark all read</button>
            )}
          </div>
          {notifications.data?.length === 0 && <p className="mt-3 text-sm text-ink-500">No notifications yet.</p>}
          <ul className="mt-3 space-y-3">
            {notifications.data?.slice(0, 15).map((n) => (
              <li key={n.id} className={`rounded-xl p-3 text-sm ${n.isRead ? 'bg-sand-50' : 'bg-amber-400/10'}`}>
                <p className="font-bold">{n.link?.startsWith('/') ? <Link to={n.link} className="hover:underline">{n.title}</Link> : n.title}</p>
                <p className="text-ink-700">{n.message}</p>
                <p className="mt-1 text-xs text-ink-500">{formatDateTime(n.createdAt)}</p>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </div>
  )
}

function ProfileForm({ profile }: { profile: Profile }) {
  const { refresh } = useAuth()
  const [status, setStatus] = useState<{ tone: 'success' | 'error'; text: string } | null>(null)
  const form = useForm<ProfileValues>({ resolver: zodResolver(profileSchema) })
  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = form

  useEffect(() => {
    reset({
      firstName: profile.user.firstName, lastName: profile.user.lastName, phoneNumber: profile.user.phoneNumber ?? '',
      addressLine: profile.addressLine ?? '', suburb: profile.suburb ?? '', city: profile.city ?? '', province: profile.province ?? '',
      postalCode: profile.postalCode ?? '', dateOfBirth: profile.dateOfBirth ?? '', driverLicenceExpiry: profile.driverLicenceExpiry ?? '',
    })
  }, [profile, reset])

  const onSubmit = handleSubmit(async (v) => {
    setStatus(null)
    const nul = (s?: string) => (s && s.trim() ? s.trim() : null)
    try {
      await api.account.updateProfile({
        firstName: v.firstName, lastName: v.lastName, phoneNumber: nul(v.phoneNumber), addressLine: nul(v.addressLine), suburb: nul(v.suburb),
        city: nul(v.city), province: nul(v.province), postalCode: nul(v.postalCode), dateOfBirth: nul(v.dateOfBirth), driverLicenceExpiry: nul(v.driverLicenceExpiry),
      })
      await refresh()
      setStatus({ tone: 'success', text: 'Profile saved.' })
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(profileSchema.shape))
      if (message) setStatus({ tone: 'error', text: message })
    }
  })

  return (
    <form className="card space-y-4 p-5" onSubmit={onSubmit} noValidate>
      <h2 className="text-lg">Profile</h2>
      <p className="text-sm text-ink-500">Signed in as {profile.user.email}</p>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="p-first" label="First name" error={errors.firstName?.message}><input id="p-first" className="input" {...register('firstName')} /></Field>
        <Field id="p-last" label="Last name" error={errors.lastName?.message}><input id="p-last" className="input" {...register('lastName')} /></Field>
        <Field id="p-phone" label="Mobile number" error={errors.phoneNumber?.message}><input id="p-phone" type="tel" className="input" {...register('phoneNumber')} /></Field>
        <Field id="p-dob" label="Date of birth" error={errors.dateOfBirth?.message} hint="Drivers must be 18 or older."><input id="p-dob" type="date" className="input" {...register('dateOfBirth')} /></Field>
        <Field id="p-licence" label="Driver's licence expiry" error={errors.driverLicenceExpiry?.message}><input id="p-licence" type="date" className="input" {...register('driverLicenceExpiry')} /></Field>
        <Field id="p-address" label="Street address" error={errors.addressLine?.message}><input id="p-address" className="input" autoComplete="street-address" {...register('addressLine')} /></Field>
        <Field id="p-suburb" label="Suburb" error={errors.suburb?.message}><input id="p-suburb" className="input" {...register('suburb')} /></Field>
        <Field id="p-city" label="City or town" error={errors.city?.message}><input id="p-city" className="input" {...register('city')} /></Field>
        <Field id="p-province" label="Province" error={errors.province?.message}>
          <select id="p-province" className="input" {...register('province')}><option value="">Choose…</option>{provinces.map((p) => <option key={p}>{p}</option>)}</select>
        </Field>
        <Field id="p-postal" label="Postal code" error={errors.postalCode?.message}><input id="p-postal" inputMode="numeric" className="input" {...register('postalCode')} /></Field>
      </div>
      {status && <Alert tone={status.tone}>{status.text}</Alert>}
      <button className="btn-dark" disabled={isSubmitting}>{isSubmitting ? 'Saving…' : 'Save profile'}</button>
    </form>
  )
}

const passwordFormSchema = z.object({ currentPassword: z.string().min(1, 'Required.'), newPassword: passwordSchema, confirm: z.string() })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'The passwords do not match.' })
type PasswordValues = z.infer<typeof passwordFormSchema>

function PasswordForm() {
  const [status, setStatus] = useState<{ tone: 'success' | 'error'; text: string } | null>(null)
  const form = useForm<PasswordValues>({ resolver: zodResolver(passwordFormSchema), defaultValues: { currentPassword: '', newPassword: '', confirm: '' } })
  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = form
  return (
    <form className="card space-y-4 p-5" noValidate onSubmit={handleSubmit(async (v) => {
      setStatus(null)
      try { await api.account.changePassword(v.currentPassword, v.newPassword); reset(); setStatus({ tone: 'success', text: 'Password changed.' }) } catch (e) {
        const message = applyServerErrors(e, form.setError, ['currentPassword', 'newPassword'])
        if (message) setStatus({ tone: 'error', text: message })
      }
    })}>
      <h2 className="text-lg">Change password</h2>
      <Field id="pw-current" label="Current password" error={errors.currentPassword?.message}><input id="pw-current" type="password" autoComplete="current-password" className="input" {...register('currentPassword')} /></Field>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="pw-new" label="New password" error={errors.newPassword?.message}><input id="pw-new" type="password" autoComplete="new-password" className="input" {...register('newPassword')} /></Field>
        <Field id="pw-confirm" label="Confirm new password" error={errors.confirm?.message}><input id="pw-confirm" type="password" autoComplete="new-password" className="input" {...register('confirm')} /></Field>
      </div>
      {status && <Alert tone={status.tone}>{status.text}</Alert>}
      <button className="btn-outline" disabled={isSubmitting}>Change password</button>
    </form>
  )
}
